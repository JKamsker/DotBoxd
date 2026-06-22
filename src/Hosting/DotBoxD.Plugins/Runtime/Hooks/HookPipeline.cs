using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

public sealed partial class HookPipeline<TEvent> : IKernelHandlerPipeline
{
    private readonly object _gate = new();

    // Copy-on-write snapshots: publish reads these references without locking or allocating,
    // while mutation replaces the whole array under _gate. Reading each reference once at the
    // start of a publish preserves stable per-publish semantics, because installed delegates
    // never mutate an existing array in place.
    private volatile Func<TEvent, HookContext, ValueTask<bool>>[] _filters = [];
    private readonly KernelHandlerSet<TEvent> _handlerSet = new();
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly HookContext _defaultContext;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;

    internal HookPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<ResultHookFault>? onFault = null)
    {
        _adapter = adapter;
        _messages = messages;
        _defaultContext = new HookContext(messages, CancellationToken.None);
        _kernels = kernels;
        _installer = installer;
        _resultHooks = new ResultHookSlot<TEvent>(adapter, onFault);
    }

    /// <summary>
    /// Installs an analyzer-generated hook-chain package and wires it into this pipeline. Called by
    /// the generated interceptor that replaces a <c>Run(lambda)</c> call site, so the lowered
    /// chain runs as verified IR instead of throwing. Blocks on install at setup time.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_installer is null)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK063",
                    "this hook pipeline has no installer; create it from a PluginServer to use generated chains.")
            ]);
        }

        var kernel = _installer(package);
        try
        {
            return Use(kernel);
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }
    }

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
        => Where((e, context) => ValueTask.FromResult(filter(e, context)));

    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        lock (_gate)
        {
            _filters = [.. _filters, filter];
        }

        return this;
    }

    /// <summary>Element-only filter — the same as the (element, context) overload with the context
    /// discarded. Both arities are always available so a stage need not take the context it doesn't use.</summary>
    public HookPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public HookPipeline<TEvent> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, HookContext, ValueTask> handler)
    {
        _handlerSet.Add(handler);
        return this;
    }

    public HookPipeline<TEvent> InvokeHostHandler(Action<TEvent, HookContext> handler)
        => InvokeHostHandler((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });

    public HookPipeline<TEvent> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public HookPipeline<TEvent> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    /// <summary>Native host terminal — runs in-process (NOT sandboxed). Use sparingly.</summary>
    public HookPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public HookPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    /// <summary>Projects the flowing element to a new type for downstream Where/terminal stages.</summary>
    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new HookStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    /// <summary>
    /// The terminal the analyzer lowers to verified IR. It never runs as host code: un-lowered it
    /// throws, so plugin logic cannot accidentally execute unsandboxed.
    /// </summary>
    public HookPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Run(Action<TEvent> handler)
        => throw HookLowering.NotLowered();

    public HookPipeline<TEvent> Use(InstalledKernel kernel)
    {
        kernel.ValidateFor(_adapter);
        _handlerSet.Add(kernel, (e, context) => kernel.InvokeAsync(_adapter, e, context.CancellationToken));
        return this;
    }

    public HookPipeline<TEvent> Use(InstalledKernelPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        pool.ValidateFor(_adapter);
        _handlerSet.Add(pool, (e, context) => pool.InvokeAsync(_adapter, e, context.CancellationToken));
        return this;
    }

    public HookPipeline<TEvent> Use<TKernel>() where TKernel : class
        => Use(_kernels.GetByKernelType<TKernel>());

    /// <summary>
    /// Wires a lowered <b>local-terminal</b> chain kernel (a remote <c>RunLocal</c> chain): the kernel's
    /// lowered <c>Where</c>/<c>Select</c> always run here in the sandbox, and for each event that passes the
    /// filter the projected value is encoded and handed to <paramref name="push"/> — the control-plane
    /// callback that delivers it across the IPC boundary to the plugin's native delegate. Non-matching events
    /// never reach <paramref name="push"/>, so filtering provably happens server-side before any IPC.
    /// </summary>
    public HookPipeline<TEvent> UseProjecting(InstalledKernel kernel, string subscriptionId, RemoteLocalPush push)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(push);
        kernel.ValidateFor(_adapter);
        var wholeEvent = LocalCallbackProjection.IsWholeEvent(kernel.Manifest);
        if (wholeEvent)
        {
            LocalCallbackProjection.EnsureWholeEventSupported(_adapter);
        }

        _handlerSet.Add(kernel, (e, context) =>
            LocalCallbackProjection.PushAsync(kernel, _adapter, e, context, wholeEvent, subscriptionId, push));

        return this;
    }

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

    void IKernelHandlerPipeline.RemoveKernel(InstalledKernel kernel)
    {
        _resultHooks.RemoveKernel(kernel);
        _handlerSet.Remove(kernel);
    }

    void IKernelHandlerPipeline.RemoveKernelPool(InstalledKernelPool pool)
        => _handlerSet.Remove(pool);

    internal ValueTask PublishAsync(TEvent e, CancellationToken cancellationToken)
    {
        // Read each copy-on-write reference once for a stable per-publish snapshot. No lock or
        // allocation: a concurrent mutation replaces the array reference rather than editing it.
        var filters = _filters;
        var handlers = _handlerSet.Snapshot;

        var context = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultContext;
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i](e, context);
            if (!filter.IsCompletedSuccessfully)
            {
                return PublishAfterFilterAwaitAsync(filter, filters, handlers, e, context, i);
            }

            if (!filter.Result)
            {
                return ValueTask.CompletedTask;
            }
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers[i](e, context);
            if (!handler.IsCompletedSuccessfully)
            {
                return PublishAfterHandlerAwaitAsync(handler, handlers, e, context, i);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static async ValueTask PublishAfterFilterAwaitAsync(
        ValueTask<bool> pending,
        Func<TEvent, HookContext, ValueTask<bool>>[] filters,
        Func<TEvent, HookContext, ValueTask>[] handlers,
        TEvent e,
        HookContext context,
        int index)
    {
        if (!await pending.ConfigureAwait(false))
        {
            return;
        }

        for (var i = index + 1; i < filters.Length; i++)
        {
            if (!await filters[i](e, context).ConfigureAwait(false))
            {
                return;
            }
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            await handlers[i](e, context).ConfigureAwait(false);
        }
    }

    private static async ValueTask PublishAfterHandlerAwaitAsync(
        ValueTask pending,
        Func<TEvent, HookContext, ValueTask>[] handlers,
        TEvent e,
        HookContext context,
        int index)
    {
        await pending.ConfigureAwait(false);
        for (var i = index + 1; i < handlers.Length; i++)
        {
            await handlers[i](e, context).ConfigureAwait(false);
        }
    }
}
