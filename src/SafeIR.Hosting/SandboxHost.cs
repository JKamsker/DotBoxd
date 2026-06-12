namespace SafeIR.Hosting;

using System.Collections.Concurrent;
using SafeIR;
using SafeIR.Compiler;
using SafeIR.Interpreter;
using SafeIR.Validation;

public sealed class SandboxHost
{
    private readonly BindingRegistry _bindings;
    private readonly ISandboxInterpreter _interpreter;
    private readonly ISandboxCompiler? _compiler;
    private readonly ConcurrentDictionary<string, int> _autoRuns = new(StringComparer.Ordinal);

    internal SandboxHost(BindingRegistry bindings, ISandboxInterpreter interpreter, ISandboxCompiler? compiler)
    {
        _bindings = bindings;
        _interpreter = interpreter;
        _compiler = compiler;
    }

    public static SandboxHost Create(Action<SandboxHostBuilder>? configure = null)
    {
        var builder = new SandboxHostBuilder();
        configure?.Invoke(builder);
        return builder.Build();
    }

    public ValueTask<ExecutionPlan> PrepareAsync(
        SandboxModule module,
        SandboxPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionPlanGuard.EnsurePolicyLimits(policy);
        var validation = new ModuleValidator().Validate(module, _bindings, policy);
        if (!validation.Succeeded) {
            throw new SandboxValidationException(validation.Diagnostics);
        }

        return ValueTask.FromResult(ExecutionPlanBuilder.Build(module, policy, _bindings, validation.Functions));
    }

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SandboxExecutionOptions();
        ExecutionPlanGuard.EnsurePrepared(plan, _bindings);
        if (options.RequireDeterministic && !plan.Policy.Deterministic) {
            return DeterminismRequiredResult(plan, options);
        }

        return options.Mode switch {
            ExecutionMode.Compiled => await ExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false),
            ExecutionMode.Interpreted => await ExecuteInterpretedAsync(
                    plan,
                    entrypoint,
                    input,
                    options,
                    cancellationToken)
                .ConfigureAwait(false),
            ExecutionMode.Auto => await ExecuteAutoAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false),
            _ => CompilerUnavailableResult(plan, options)
        };
    }

    private async ValueTask<SandboxExecutionResult> ExecuteAutoAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (_compiler is null || options.EnableDebugTrace) {
            return await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        }
        var key = plan.PlanHash + "|" + entrypoint;
        var count = _autoRuns.AddOrUpdate(key, 1, (_, current) => current + 1);
        var threshold = Math.Max(1, options.AutoCompileThreshold);
        if (count < threshold) {
            return await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        }
        return await ExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<SandboxExecutionResult> ExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (_compiler is null || options.EnableDebugTrace) {
            var reason = _compiler is null ? CompilerUnavailableError() : DebugTraceFallbackError();
            return options.AllowFallbackToInterpreter
                ? await ExecuteFallbackToInterpreterAsync(
                        plan,
                        entrypoint,
                        input,
                        options,
                        reason,
                        cancellationToken)
                    .ConfigureAwait(false)
                : CompilerUnavailableResult(plan, options);
        }

        var compiled = await TryExecuteCompiledAsync(plan, entrypoint, input, options, cancellationToken)
            .ConfigureAwait(false);
        if (compiled.Result is not null) {
            return compiled.Result;
        }

        return await ExecuteFallbackToInterpreterAsync(
                plan,
                entrypoint,
                input,
                options,
                compiled.FallbackReason ?? CompilerUnavailableError(),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<SandboxExecutionResult> ExecuteFallbackToInterpreterAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        SandboxError reason,
        CancellationToken cancellationToken)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var fallbackOptions = options with { RunId = runId };
        var result = await ExecuteInterpretedAsync(plan, entrypoint, input, fallbackOptions, cancellationToken)
            .ConfigureAwait(false);
        var audit = new[] { FallbackAudit(plan, runId, reason) }
            .Concat(result.AuditEvents)
            .ToArray();
        return result with { AuditEvents = audit };
    }

    private async ValueTask<SandboxExecutionResult> ExecuteInterpretedAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => await _interpreter.ExecuteAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false);

    private async ValueTask<CompiledAttempt> TryExecuteCompiledAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        try {
            var artifact = await _compiler!.CompileAsync(plan, new CompileOptions(entrypoint), cancellationToken).ConfigureAwait(false);
            var executable = await CompiledArtifactGuard.MaterializeExecutableAsync(artifact, plan, entrypoint, cancellationToken)
                .ConfigureAwait(false);
            var result = await CompiledExecutionRunner.ExecuteAsync(executable, plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
            return new CompiledAttempt(result, null);
        }
        catch (SandboxRuntimeException ex) when (CanFallback(options, ex)) {
            return new CompiledAttempt(null, ex.Error);
        }
        catch (SandboxRuntimeException ex) {
            return new CompiledAttempt(CompiledFailureResult(plan, options, ex.Error), null);
        }
        catch (OperationCanceledException) {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return new CompiledAttempt(CompiledFailureResult(plan, options, error), null);
        }
        catch (Exception) {
            var error = new SandboxError(SandboxErrorCode.HostFailure, "compiled execution failed");
            return new CompiledAttempt(CompiledFailureResult(plan, options, error), null);
        }
    }

    private static bool CanFallback(SandboxExecutionOptions options, SandboxRuntimeException ex)
        => options.AllowFallbackToInterpreter &&
           ex.Error.Code is SandboxErrorCode.VerifierFailure or SandboxErrorCode.ValidationError;

    private static SandboxError CompilerUnavailableError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");

    private static SandboxError DebugTraceFallbackError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is disabled while debug tracing is enabled");

    private static SandboxAuditEvent FallbackAudit(ExecutionPlan plan, SandboxRunId runId, SandboxError reason)
        => new(
            runId,
            "ExecutionFallback",
            DateTimeOffset.UtcNow,
            true,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: reason.Code,
            Message: $"compiled execution fell back to interpreted mode: {reason.SafeMessage}");

    private static SandboxExecutionResult CompilerUnavailableResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var error = new SandboxError(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompilerUnavailable",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));

        return new SandboxExecutionResult {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult CompiledFailureResult(
        ExecutionPlan plan,
        SandboxExecutionOptions options,
        SandboxError error)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "CompiledExecutionFailed",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));
        return new SandboxExecutionResult {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = ExecutionMode.Compiled,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private static SandboxExecutionResult DeterminismRequiredResult(ExecutionPlan plan, SandboxExecutionOptions options)
    {
        var runId = options.RunId ?? SandboxRunId.New();
        var budget = new ResourceMeter(plan.Budget);
        var error = new SandboxError(SandboxErrorCode.PolicyDenied, "deterministic execution is required");
        var audit = new InMemoryAuditSink();
        audit.Write(new SandboxAuditEvent(
            runId,
            "PolicyDenied",
            DateTimeOffset.UtcNow,
            false,
            ResourceId: $"module:{plan.ModuleHash}",
            ErrorCode: error.Code,
            Message: error.SafeMessage));

        return new SandboxExecutionResult {
            Succeeded = false,
            Error = error,
            ResourceUsage = budget.Snapshot(),
            AuditEvents = audit.Events,
            ActualMode = options.Mode,
            ModuleHash = plan.ModuleHash,
            PlanHash = plan.PlanHash,
            PolicyHash = plan.PolicyHash
        };
    }

    private sealed record CompiledAttempt(SandboxExecutionResult? Result, SandboxError? FallbackReason);
}
