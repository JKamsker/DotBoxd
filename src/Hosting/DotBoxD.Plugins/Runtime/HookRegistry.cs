using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime;

internal interface IKernelHandlerPipeline
{
    void RemoveKernel(InstalledKernel kernel);
    void RemoveKernelPool(InstalledKernelPool pool);
}

internal interface IHookPipeline<TEvent> : IKernelHandlerPipeline
{
    bool UsesAdapter(IPluginEventAdapter<TEvent> adapter);
    Hooks.IResultHookRegistration<TEvent>[] ResultRegistrations();
    ValueTask PublishAsync(TEvent e, CancellationToken cancellationToken);
    ValueTask<TResult?> FireResultAsync<TResult>(TEvent e, CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult;
    ValueTask<TResult?> FireResultAsync<TResult>(
        TEvent e,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult;
}

public sealed partial class HookRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<PipelineKey, object> _pipelines = [];
    private readonly Dictionary<Type, (object? Single, CachedPipelineFanout Multiple)> _pipelineFanout = [];
    private readonly HashSet<Type> _pipelineEventTypes = [];
    private readonly IPluginMessageSink _messages;
    private readonly PluginEventAdapterRegistry _events;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;
    private readonly Action<ResultHookFault>? _onFault;
    private readonly Action? _throwIfDisposed;
    private long _resultOrder;

    internal HookRegistry(
        IPluginMessageSink messages,
        PluginEventAdapterRegistry events,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<ResultHookFault>? onFault = null,
        Action? throwIfDisposed = null)
    {
        _messages = messages;
        _events = events;
        _kernels = kernels;
        _installer = installer;
        _onFault = onFault;
        _throwIfDisposed = throwIfDisposed;
    }

    public HookPipeline<TEvent, HookContext> On<TEvent>()
    {
        ThrowIfDisposed();
        var adapter = _events.Resolve<TEvent>();
        return On(adapter);
    }

    public HookPipeline<TEvent, HookContext> On<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ThrowIfDisposed();
        return OnHookContext(adapter, ServerContextFactory<HookContext>.Identity);
    }

    public HookPipeline<TEvent, TContext> On<TEvent, TContext>(Func<HookContext, TContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        ThrowIfDisposed();
        var adapter = _events.Resolve<TEvent>();
        return On(adapter, createContext);
    }

    public HookPipeline<TEvent, TContext> On<TEvent, TContext>(
        IPluginEventAdapter<TEvent> adapter,
        Func<HookContext, TContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(createContext);
        ThrowIfDisposed();
        if (typeof(TContext) == typeof(HookContext))
        {
            return (HookPipeline<TEvent, TContext>)(object)OnHookContext(adapter, (Func<HookContext, HookContext>)(object)createContext);
        }

        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
            var key = new PipelineKey(typeof(TEvent), typeof(TContext));
            if (_pipelines.TryGetValue(key, out var existing))
            {
                var pipeline = (HookPipeline<TEvent, TContext>)existing;
                EnsureContextFactoryMatches(pipeline.UsesContextFactory, createContext, "hook");
                return pipeline;
            }

            var created = new HookPipeline<TEvent, TContext>(
                adapter,
                _messages,
                new ServerContextFactory<TContext>(createContext),
                _kernels,
                _installer,
                _onFault,
                NextResultOrder);
            _pipelines[key] = created;
            RegisterEventTypeLocked<TEvent>();
            return created;
        }
    }

    private HookPipeline<TEvent, HookContext> OnHookContext<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        Func<HookContext, HookContext> createContext)
    {
        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
            var key = new PipelineKey(typeof(TEvent), typeof(HookContext));
            if (_pipelines.TryGetValue(key, out var existing))
            {
                var pipeline = (HookPipeline<TEvent, HookContext>)existing;
                EnsureContextFactoryMatches(pipeline.UsesContextFactory, createContext, "hook");
                return pipeline;
            }

            var created = new HookPipeline<TEvent, HookContext>(
                adapter,
                _messages,
                new ServerContextFactory<HookContext>(createContext),
                _kernels,
                _installer,
                _onFault,
                NextResultOrder);
            _pipelines[key] = created;
            RegisterEventTypeLocked<TEvent>();
            return created;
        }
    }

    private long NextResultOrder()
        => Interlocked.Increment(ref _resultOrder) - 1;

    internal void EnsureCanRegister<TEvent>(IPluginEventAdapter<TEvent> adapter)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            EnsureCanRegisterLocked(adapter);
        }
    }

    internal void RemoveKernel(InstalledKernel kernel)
    {
        object[] pipelines;
        lock (_gate)
        {
            pipelines = [.. _pipelines.Values];
        }

        foreach (var pipeline in pipelines)
        {
            ((IKernelHandlerPipeline)pipeline).RemoveKernel(kernel);
        }
    }

    internal void RemoveKernelPool(InstalledKernelPool pool)
    {
        object[] pipelines;
        lock (_gate)
        {
            pipelines = [.. _pipelines.Values];
        }

        foreach (var pipeline in pipelines)
        {
            ((IKernelHandlerPipeline)pipeline).RemoveKernelPool(pool);
        }
    }

    public ValueTask PublishAsync<TEvent>(TEvent e, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        object? single;
        CachedPipelineFanout multiple;
        lock (_gate)
        {
            (single, multiple) = PipelinesForEventLocked<TEvent>();
        }

        if (multiple.Count > 0)
        {
            return PublishManyAsync(multiple, e, cancellationToken);
        }

        return single is null
            ? ValueTask.CompletedTask
            : ((IHookPipeline<TEvent>)single).PublishAsync(e, cancellationToken);
    }

    /// <summary>
    /// Dispatches the result-returning hooks (<c>.Register(...)</c> / <c>.RegisterLocal(...)</c>) installed for
    /// <typeparamref name="TContext"/> in descending priority order, returning the first successful
    /// <typeparamref name="TResult"/> or <see langword="null"/> when none is registered or none succeeds. The
    /// host applies the returned result to its live state. The result type is named explicitly here because the
    /// host — unlike the plugin authoring side, where it is inferred from the <c>[Hook]</c> context — already
    /// knows the type it will apply.
    /// </summary>
    public ValueTask<TResult?> FireAsync<TContext, TResult>(
        TContext context,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        object? single;
        CachedPipelineFanout multiple;
        lock (_gate)
        {
            (single, multiple) = PipelinesForEventLocked<TContext>();
        }

        ValidateResultType<TContext, TResult>();
        if (multiple.Count > 0)
        {
            return FireManyAsync<TContext, TResult>(multiple, context, cancellationToken);
        }

        return single is null
            ? new ValueTask<TResult?>((TResult?)null)
            : ((IHookPipeline<TContext>)single).FireResultAsync<TResult>(context, cancellationToken);
    }

    public ValueTask<TResult?> FireAsync<TContext, TResult>(
        TContext context,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        object? single;
        CachedPipelineFanout multiple;
        lock (_gate)
        {
            (single, multiple) = PipelinesForEventLocked<TContext>();
        }

        ValidateResultType<TContext, TResult>();
        if (multiple.Count > 0)
        {
            return FireManyAsync(multiple, context, options, cancellationToken);
        }

        return single is null
            ? new ValueTask<TResult?>((TResult?)null)
            : ((IHookPipeline<TContext>)single).FireResultAsync(context, options, cancellationToken);
    }

    private static void ValidateResultType<TContext, TResult>()
        where TResult : struct, IHookResult
    {
        var hook = ResultTypeCache<TContext>.Attr;
        if (hook is null || hook.ResultType == typeof(TResult))
        {
            return;
        }

        throw new SandboxValidationException([
            new SandboxDiagnostic(
                "DBXK066",
                $"Hook context '{typeof(TContext).Name}' declares result type '{hook.ResultType.Name}', " +
                $"but FireAsync was called with '{typeof(TResult).Name}'.")
        ]);
    }

    // The [Hook] attribute on a context type is fixed at compile time; cache the lookup per closed TContext so
    // FireAsync stays allocation-free on the hot path — including the no-handler fast path it guards, which
    // would otherwise pay a reflection lookup + allocation before the pipeline-null early return.
    private static class ResultTypeCache<TContext>
    {
        internal static readonly HookAttribute? Attr =
            (HookAttribute?)Attribute.GetCustomAttribute(typeof(TContext), typeof(HookAttribute), inherit: false);
    }

    private void ThrowIfDisposed()
        => _throwIfDisposed?.Invoke();

    private readonly record struct PipelineKey(Type EventType, Type ContextType);
}
