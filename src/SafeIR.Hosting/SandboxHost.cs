namespace SafeIR.Hosting;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using SafeIR;
using SafeIR.Compiler;
using SafeIR.Interpreter;
using SafeIR.Validation;

public sealed partial class SandboxHost
{
    private readonly BindingRegistry _bindings;
    private readonly ISandboxInterpreter _interpreter;
    private readonly ISandboxCompiler? _compiler;
    private readonly IExecutionModeSelector _modeSelector;
    private readonly Action<SandboxAuditEvent>? _auditObserver;
    private readonly byte[] _planSigningKey = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, int> _autoRuns = new(StringComparer.Ordinal);

    internal SandboxHost(
        BindingRegistry bindings,
        ISandboxInterpreter interpreter,
        ISandboxCompiler? compiler,
        IExecutionModeSelector modeSelector,
        Action<SandboxAuditEvent>? auditObserver)
    {
        _bindings = bindings;
        _interpreter = interpreter;
        _compiler = compiler;
        _modeSelector = modeSelector;
        _auditObserver = auditObserver;
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
        if (!validation.Succeeded)
        {
            throw new SandboxValidationException(validation.Diagnostics);
        }

        return ValueTask.FromResult(ExecutionPlanBuilder.Build(module, policy, _bindings, validation.Functions, _planSigningKey));
    }

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SandboxExecutionOptions();
        ExecutionPlanGuard.EnsurePrepared(plan, _bindings, _planSigningKey);
        if (TryGetRevokedCapability(plan, entrypoint, out var revoked))
        {
            return Publish(CapabilityRevokedResult(plan, options, revoked));
        }

        if (options.Isolation == SandboxIsolation.WorkerProcess)
        {
            return Publish(WorkerIsolationUnavailableResult(plan, options));
        }

        if (options.RequireDeterministic && !plan.Policy.Deterministic)
        {
            return Publish(DeterminismRequiredResult(plan, options));
        }

        var result = options.Mode switch
        {
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
        return Publish(result);
    }

    private SandboxExecutionResult Publish(SandboxExecutionResult result)
    {
        if (_auditObserver is not null)
        {
            foreach (var auditEvent in result.AuditEvents)
            {
                _auditObserver(auditEvent);
            }
        }

        return result;
    }

    private async ValueTask<SandboxExecutionResult> ExecuteAutoAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (_compiler is null || options.EnableDebugTrace)
        {
            return await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        }
        var key = plan.PlanHash + "|" + entrypoint;
        var count = _autoRuns.AddOrUpdate(key, 1, (_, current) => current + 1);
        if (count == 1)
        {
            return await ExecuteInterpretedAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
        }

        var decision = _modeSelector.Choose(
            plan,
            options,
            new ModuleHotnessStats(count),
            SafeIR.Compiler.CompiledCacheStatus.None);
        if (decision.Mode == ExecutionMode.Interpreted || decision.Mode == ExecutionMode.Auto)
        {
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
        if (_compiler is null || options.EnableDebugTrace)
        {
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
        if (compiled.Result is not null)
        {
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
        var audit = FallbackSecurityAudits(plan, runId, reason)
            .Concat([FallbackAudit(plan, runId, reason)])
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
        try
        {
            var artifact = await _compiler!.CompileAsync(plan, new CompileOptions(entrypoint), cancellationToken).ConfigureAwait(false);
            var executable = await CompiledArtifactGuard.MaterializeExecutableAsync(artifact, plan, entrypoint, cancellationToken)
                .ConfigureAwait(false);
            var result = await CompiledExecutionRunner.ExecuteAsync(executable, plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
            return new CompiledAttempt(result, null);
        }
        catch (SandboxRuntimeException ex) when (CanFallback(options, ex))
        {
            return new CompiledAttempt(null, ex.Error);
        }
        catch (SandboxRuntimeException ex)
        {
            return new CompiledAttempt(CompiledFailureResult(plan, options, ex.Error), null);
        }
        catch (OperationCanceledException)
        {
            var error = new SandboxError(SandboxErrorCode.Cancelled, "execution cancelled");
            return new CompiledAttempt(CompiledFailureResult(plan, options, error), null);
        }
        catch (Exception)
        {
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

    private sealed record CompiledAttempt(SandboxExecutionResult? Result, SandboxError? FallbackReason);
}
