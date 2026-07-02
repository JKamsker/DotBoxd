using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public partial class HookPipeline<TEvent, TContext> : IHookPipeline<TEvent>
{
    private readonly object _gate = new();

    // Copy-on-write snapshots: publish reads these references without locking or allocating,
    // while mutation replaces the whole array under _gate. Reading each reference once at the
    // start of a publish preserves stable per-publish semantics, because installed delegates
    // never mutate an existing array in place.
    private volatile Func<TEvent, TContext, ValueTask<bool>>[] _filters = [];
    private readonly KernelHandlerSet<TEvent, TContext> _handlerSet = new();
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly HookContext _defaultRawContext;
    private readonly ServerContextFactory<TContext> _contextFactory;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal HookPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        ServerContextFactory<TContext> contextFactory,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<ResultHookFault>? onFault = null,
        Func<long>? nextResultOrder = null)
    {
        _adapter = adapter;
        _messages = messages;
        _defaultRawContext = new HookContext(messages, CancellationToken.None);
        _contextFactory = contextFactory;
        _kernels = kernels;
        _installer = installer;
        _resultHooks = new ResultHookSlot<TEvent, TContext>(adapter, onFault, nextResultOrder);
    }

    public HookPipeline<TEvent, TContext> Where(Func<TEvent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, context) => ValueTask.FromResult(filter(e, context)));
    }

    public HookPipeline<TEvent, TContext> Where(Func<TEvent, TContext, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        lock (_gate)
        {
            _filters = [.. _filters, filter];
        }

        return this;
    }

    /// <summary>Element-only filter — the same as the (element, context) overload with the context
    /// discarded. Both arities are always available so a stage need not take the context it doesn't use.</summary>
    public HookPipeline<TEvent, TContext> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public HookPipeline<TEvent, TContext> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Func<TEvent, TContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlerSet.Add((e, _, context) => handler(e, context));
        return this;
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Action<TEvent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public HookPipeline<TEvent, TContext> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    /// <summary>Native host terminal — runs in-process (NOT sandboxed). Use sparingly.</summary>
    public HookPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    /// <summary>Projects the flowing element to a new type for downstream Where/terminal stages.</summary>
    public HookStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext, TContext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public HookStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    /// <summary>
    /// The terminal the analyzer lowers to verified IR. It never runs as host code: un-lowered it
    /// throws, so plugin logic cannot accidentally execute unsandboxed.
    /// </summary>
    public HookPipeline<TEvent, TContext> Run(Func<TEvent, TContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Action<TEvent, TContext> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Func<TEvent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Run(Action<TEvent> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent, TContext> Use(InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        kernel.ValidateFor(_adapter);
        _handlerSet.Add(kernel, (e, rawContext, _) => kernel.InvokeAsync(_adapter, e, rawContext.CancellationToken));
        return this;
    }

    public HookPipeline<TEvent, TContext> Use(InstalledKernelPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        pool.ValidateFor(_adapter);
        _handlerSet.Add(pool, (e, rawContext, _) => pool.InvokeAsync(_adapter, e, rawContext.CancellationToken));
        return this;
    }

    public HookPipeline<TEvent, TContext> Use<TKernel>() where TKernel : class
        => Use(_kernels.GetByKernelType<TKernel>());

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

    internal bool UsesContextFactory(Func<HookContext, TContext> createContext)
        => _contextFactory.Uses(createContext);

    bool IHookPipeline<TEvent>.UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => UsesAdapter(adapter);

    void IKernelHandlerPipeline.RemoveKernel(InstalledKernel kernel)
    {
        _resultHooks.RemoveKernel(kernel);
        _handlerSet.Remove(kernel);
    }

    void IKernelHandlerPipeline.RemoveKernelPool(InstalledKernelPool pool)
        => _handlerSet.Remove(pool);

    internal ValueTask PublishAsync(TEvent e, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Read each copy-on-write reference once for a stable per-publish snapshot. No lock or
        // allocation: a concurrent mutation replaces the array reference rather than editing it.
        var filters = _filters;
        var handlers = _handlerSet.Snapshot;

        var rawContext = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultRawContext;
        var context = _contextFactory.Create(rawContext);
        for (var i = 0; i < filters.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filter = filters[i](e, context);
            if (!filter.IsCompletedSuccessfully)
            {
                return PublishAfterFilterAwaitAsync(filter, filters, handlers, e, rawContext, context, i);
            }

            if (!filter.Result)
            {
                return ValueTask.CompletedTask;
            }
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handler = handlers[i](e, rawContext, context);
            if (!handler.IsCompletedSuccessfully)
            {
                return PublishAfterHandlerAwaitAsync(handler, handlers, e, rawContext, context, i);
            }
        }

        return ValueTask.CompletedTask;
    }

    ValueTask IHookPipeline<TEvent>.PublishAsync(TEvent e, CancellationToken cancellationToken)
        => PublishAsync(e, cancellationToken);

    private static async ValueTask PublishAfterFilterAwaitAsync(
        ValueTask<bool> pending,
        Func<TEvent, TContext, ValueTask<bool>>[] filters,
        Func<TEvent, HookContext, TContext, ValueTask>[] handlers,
        TEvent e,
        HookContext rawContext,
        TContext context,
        int index)
    {
        var matched = await pending.ConfigureAwait(false);
        rawContext.CancellationToken.ThrowIfCancellationRequested();
        if (!matched)
        {
            return;
        }

        for (var i = index + 1; i < filters.Length; i++)
        {
            rawContext.CancellationToken.ThrowIfCancellationRequested();
            if (!await filters[i](e, context).ConfigureAwait(false))
            {
                return;
            }
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            rawContext.CancellationToken.ThrowIfCancellationRequested();
            await handlers[i](e, rawContext, context).ConfigureAwait(false);
        }
    }

    private static async ValueTask PublishAfterHandlerAwaitAsync(
        ValueTask pending,
        Func<TEvent, HookContext, TContext, ValueTask>[] handlers,
        TEvent e,
        HookContext rawContext,
        TContext context,
        int index)
    {
        await pending.ConfigureAwait(false);
        rawContext.CancellationToken.ThrowIfCancellationRequested();
        for (var i = index + 1; i < handlers.Length; i++)
        {
            rawContext.CancellationToken.ThrowIfCancellationRequested();
            await handlers[i](e, rawContext, context).ConfigureAwait(false);
        }
    }
}
