using System.Security.Cryptography;
using DotBoxD.Hosting.Execution.Compiled;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Hosting.Execution;

public sealed partial class SandboxHost : IDisposable
{
    private static readonly SandboxExecutionOptions DefaultExecutionOptions = new();

    private readonly BindingRegistry _bindings;
    private readonly ISandboxInterpreter _interpreter;
    private readonly CompiledExecutionProvider _compiled;
    private readonly IExecutionModeSelector _modeSelector;
    private readonly Action<SandboxAuditEvent>[] _auditObservers;
    private readonly SandboxWorkerExecutor _workerExecutor;
    private readonly byte[] _planSigningKey = RandomNumberGenerator.GetBytes(32);
    private readonly AutoExecutionHotness _autoHotness = new();
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
        _compiled = new CompiledExecutionProvider(compiler);
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

    internal IReadOnlyList<string> GetRequiredCapabilities(SandboxModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        ThrowIfDisposed();

        var validation = new ModuleValidator().Validate(module, _bindings);
        if (!validation.Succeeded)
        {
            throw new SandboxValidationException(validation.Diagnostics);
        }

        var required = new SortedSet<string>(validation.RequiredCapabilities, StringComparer.Ordinal);
        foreach (var request in module.CapabilityRequests)
        {
            required.Add(request.Id);
        }

        return required.ToArray();
    }

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        options ??= DefaultExecutionOptions;
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

        if (TryGetCapabilityDenial(plan, entrypoint, out var denial))
        {
            return Publish(CapabilityDeniedResult(plan, options, denial));
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
#pragma warning disable RCS1075 // Intentional isolation boundary: audit-observer failures must never alter sandbox execution.
            catch (Exception)
            {
                // Operational forwarding failures must not change sandbox execution behavior.
            }
#pragma warning restore RCS1075
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

    private async ValueTask<SandboxExecutionResult> ExecuteInterpretedAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
        => await _interpreter.ExecuteAsync(plan, entrypoint, input, options, cancellationToken).ConfigureAwait(false);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _compiled.Dispose();
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
