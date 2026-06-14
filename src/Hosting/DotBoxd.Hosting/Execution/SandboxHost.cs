namespace DotBoxd.Hosting;

using System.Security.Cryptography;
using DotBoxd.Kernels;
using DotBoxd.Kernels.Compiler;
using DotBoxd.Kernels.Interpreter;
using DotBoxd.Kernels.Validation;

public sealed partial class SandboxHost : IDisposable
{
    private readonly BindingRegistry _bindings;
    private readonly ISandboxInterpreter _interpreter;
    private readonly ISandboxCompiler? _compiler;
    private readonly IExecutionModeSelector _modeSelector;
    private readonly Action<SandboxAuditEvent>[] _auditObservers;
    private readonly SandboxWorkerExecutor _workerExecutor;
    private readonly byte[] _planSigningKey = RandomNumberGenerator.GetBytes(32);
    private readonly AutoExecutionHotness _autoHotness = new();
    private readonly CompiledExecutableCache _compiledExecutables = new();
    private readonly PreparedPlanIntegrityCache _preparedPlans = new();
    private int _disposed;

    internal SandboxHost(
        BindingRegistry bindings,
        ISandboxInterpreter interpreter,
        ISandboxCompiler? compiler,
        IExecutionModeSelector modeSelector,
        Action<SandboxAuditEvent>? auditObserver,
        ConfiguredSandboxWorker? worker)
    {
        _bindings = bindings;
        _interpreter = interpreter;
        _compiler = compiler;
        _modeSelector = modeSelector;
        _auditObservers = SnapshotAuditObservers(auditObserver);
        _workerExecutor = new SandboxWorkerExecutor(worker);
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
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionPlanGuard.EnsurePolicyLimits(policy);
        var validation = new ModuleValidator().Validate(module, _bindings, policy);
        if (!validation.Succeeded)
        {
            throw new SandboxValidationException(validation.Diagnostics);
        }

        var plan = ExecutionPlanBuilder.Build(
            module,
            policy,
            _bindings,
            validation.Functions,
            validation.BindingReferences,
            _planSigningKey);
        _preparedPlans.Register(plan);
        return ValueTask.FromResult(plan);
    }

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        options ??= new SandboxExecutionOptions();
        ExecutionPlanGuard.EnsurePrepared(plan, _bindings, _planSigningKey, _preparedPlans);
        if (!Enum.IsDefined(options.Mode))
        {
            return Publish(InvalidExecutionOptionsResult(
                plan,
                options,
                $"execution mode '{(int)options.Mode}' is not supported"));
        }

        if (!Enum.IsDefined(options.Isolation))
        {
            return Publish(InvalidExecutionOptionsResult(
                plan,
                options,
                $"sandbox isolation '{(int)options.Isolation}' is not supported"));
        }

        if (TryGetRevokedCapability(plan, entrypoint, out var revoked))
        {
            return Publish(CapabilityRevokedResult(plan, options, revoked));
        }

        if (options.RequireDeterministic && !plan.Policy.Deterministic)
        {
            return Publish(DeterminismRequiredResult(plan, options));
        }

        if (options.Isolation == SandboxIsolation.WorkerProcess)
        {
            var workerResult = await _workerExecutor.ExecuteAsync(plan, entrypoint, input, options, cancellationToken)
                .ConfigureAwait(false);
            return Publish(workerResult);
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
        if (_auditObservers.Length == 0)
        {
            return result;
        }

        foreach (var auditEvent in result.AuditEvents)
        {
            PublishToAuditObservers(auditEvent);
        }

        return result;
    }

    private void PublishToAuditObservers(SandboxAuditEvent auditEvent)
    {
        // The observer set is fixed for the lifetime of the host, so dispatch reuses the
        // snapshot captured at construction instead of materializing the multicast invocation
        // list per audit event.
        foreach (var observer in _auditObservers)
        {
            try
            {
                observer(auditEvent);
            }
            catch (Exception)
            {
                // Operational forwarding failures must not change sandbox execution behavior.
            }
        }
    }

    private static Action<SandboxAuditEvent>[] SnapshotAuditObservers(Action<SandboxAuditEvent>? auditObserver)
    {
        if (auditObserver is null)
        {
            return [];
        }

        var observers = auditObserver.GetInvocationList();
        var snapshot = new Action<SandboxAuditEvent>[observers.Length];
        for (var i = 0; i < observers.Length; i++)
        {
            snapshot[i] = (Action<SandboxAuditEvent>)observers[i];
        }

        return snapshot;
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
                : CompilerUnavailableResult(plan, options, reason);
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
            .ToSequencedArray();
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
            var executable = await _compiledExecutables.GetAsync(artifact, plan, entrypoint, cancellationToken)
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

    private static bool CanCompileEntrypoint(ExecutionPlan plan, string entrypoint)
        => plan.FunctionAnalysis.TryGetValue(entrypoint, out var analysis) &&
           (analysis.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;

    private static SandboxError CompilerUnavailableError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is not available for this run");

    private static SandboxError DebugTraceFallbackError()
        => new(SandboxErrorCode.ValidationError, "compiled execution is disabled while debug tracing is enabled");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _compiledExecutables.Dispose();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private sealed record CompiledAttempt(SandboxExecutionResult? Result, SandboxError? FallbackReason);
}
