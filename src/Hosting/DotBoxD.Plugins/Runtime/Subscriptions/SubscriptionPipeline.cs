using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public sealed class SubscriptionPipeline<TEvent> : IKernelHandlerPipeline
{
    private readonly object _gate = new();
    private volatile Func<TEvent, HookContext, ValueTask<bool>>[] _filters = [];
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly HookContext _defaultContext;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;
    private readonly Action<SubscriptionDeliveryFault>? _onFault;
    private readonly KernelHandlerSet<TEvent> _handlerSet = new();

    internal SubscriptionPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<SubscriptionDeliveryFault>? onFault = null)
    {
        _adapter = adapter;
        _messages = messages;
        _defaultContext = new HookContext(messages, CancellationToken.None);
        _kernels = kernels;
        _installer = installer;
        _onFault = onFault;
    }

    public SubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_installer is null)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK065",
                    "this subscription pipeline has no installer; create it from a PluginServer to use generated chains.")
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

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter)
        => Where((e, context) => ValueTask.FromResult(filter(e, context)));

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, HookContext, ValueTask<bool>> filter)
    {
        lock (_gate)
        {
            _filters = [.. _filters, filter];
        }

        return this;
    }

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public SubscriptionPipeline<TEvent> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Func<TEvent, HookContext, ValueTask> handler)
    {
        _handlerSet.Add(handler);
        return this;
    }

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Action<TEvent, HookContext> handler)
        => InvokeHostHandler((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public SubscriptionPipeline<TEvent> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public SubscriptionPipeline<TEvent> RunLocal(Func<TEvent, HookContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent> RunLocal(Action<TEvent, HookContext> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    public SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new SubscriptionStage<TEvent, TNext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public SubscriptionStage<TEvent, TNext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    public SubscriptionPipeline<TEvent> Run(Func<TEvent, HookContext, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Action<TEvent, HookContext> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Func<TEvent, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Run(Action<TEvent> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent> Use(InstalledKernel kernel)
    {
        kernel.ValidateFor(_adapter);
        _handlerSet.Add(kernel, (e, context) => kernel.InvokeAsync(_adapter, e, context.CancellationToken));
        return this;
    }

    public SubscriptionPipeline<TEvent> Use(InstalledKernelPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        pool.ValidateFor(_adapter);
        _handlerSet.Add(pool, (e, context) => pool.InvokeAsync(_adapter, e, context.CancellationToken));
        return this;
    }

    public SubscriptionPipeline<TEvent> Use<TKernel>() where TKernel : class
        => Use(_kernels.GetByKernelType<TKernel>());

    /// <summary>
    /// Wires a lowered <b>local-terminal</b> subscription chain (a remote <c>RunLocal</c> chain): the lowered
    /// <c>Where</c>/<c>Select</c> always run here in the sandbox, and for each event that passes the filter the
    /// projected value is encoded and handed to <paramref name="push"/> for delivery across the IPC boundary to
    /// the plugin's native delegate. Non-matching events never reach <paramref name="push"/>.
    /// </summary>
    public SubscriptionPipeline<TEvent> UseProjecting(InstalledKernel kernel, string subscriptionId, Hooks.RemoteLocalPush push)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentException.ThrowIfNullOrEmpty(subscriptionId);
        ArgumentNullException.ThrowIfNull(push);
        kernel.ValidateFor(_adapter);
        var wholeEvent = Hooks.LocalCallbackProjection.IsWholeEvent(kernel.Manifest);
        if (wholeEvent)
        {
            Hooks.LocalCallbackProjection.EnsureWholeEventSupported(_adapter);
        }

        _handlerSet.Add(kernel, (e, context) =>
            Hooks.LocalCallbackProjection.PushAsync(kernel, _adapter, e, context, wholeEvent, subscriptionId, push));

        return this;
    }

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

    void IKernelHandlerPipeline.RemoveKernel(InstalledKernel kernel)
        => _handlerSet.Remove(kernel);

    void IKernelHandlerPipeline.RemoveKernelPool(InstalledKernelPool pool)
        => _handlerSet.Remove(pool);

    internal void Publish(TEvent e, CancellationToken cancellationToken)
    {
        var filters = _filters;
        var handlers = _handlerSet.Snapshot;
        if (filters.Length == 0 && handlers.Length == 0)
        {
            return;
        }

        var context = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultContext;
        var onFault = _onFault;
        _ = Task.Run(() => SubscriptionDelivery.PublishSafelyAsync(filters, handlers, e, context, onFault).AsTask());
    }
}
