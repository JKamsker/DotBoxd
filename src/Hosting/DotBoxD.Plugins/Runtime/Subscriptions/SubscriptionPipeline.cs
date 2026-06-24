using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

public class SubscriptionPipeline<TEvent, TContext> : ISubscriptionPipeline<TEvent>
{
    private readonly object _gate = new();
    private volatile Func<TEvent, TContext, ValueTask<bool>>[] _filters = [];
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly IPluginMessageSink _messages;
    private readonly HookContext _defaultRawContext;
    private readonly ServerContextFactory<TContext> _contextFactory;
    private readonly KernelRegistry _kernels;
    private readonly Func<PluginPackage, InstalledKernel>? _installer;
    private readonly Action<SubscriptionDeliveryFault>? _onFault;
    private readonly KernelHandlerSet<TEvent, TContext> _handlerSet = new();

    internal SubscriptionPipeline(
        IPluginEventAdapter<TEvent> adapter,
        IPluginMessageSink messages,
        ServerContextFactory<TContext> contextFactory,
        KernelRegistry kernels,
        Func<PluginPackage, InstalledKernel>? installer = null,
        Action<SubscriptionDeliveryFault>? onFault = null)
    {
        _adapter = adapter;
        _messages = messages;
        _defaultRawContext = new HookContext(messages, CancellationToken.None);
        _contextFactory = contextFactory;
        _kernels = kernels;
        _installer = installer;
        _onFault = onFault;
    }

    public SubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
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

    public SubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, TContext, bool> filter)
        => Where((e, context) => ValueTask.FromResult(filter(e, context)));

    public SubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, TContext, ValueTask<bool>> filter)
    {
        lock (_gate)
        {
            _filters = [.. _filters, filter];
        }

        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public SubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, ValueTask<bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Where((e, _) => filter(e));
    }

    public SubscriptionPipeline<TEvent, TContext> InvokeHostHandler(Func<TEvent, TContext, ValueTask> handler)
    {
        _handlerSet.Add((e, _, context) => handler(e, context));
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> InvokeHostHandler(Action<TEvent, TContext> handler)
        => InvokeHostHandler((e, context) =>
        {
            handler(e, context);
            return ValueTask.CompletedTask;
        });

    public SubscriptionPipeline<TEvent, TContext> InvokeHostHandler(Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public SubscriptionPipeline<TEvent, TContext> InvokeHostHandler(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InvokeHostHandler((e, _) => handler(e));
    }

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
        => InvokeHostHandler(handler);

    public SubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
        => InvokeHostHandler(handler);

    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new SubscriptionStage<TEvent, TNext, TContext>(
            this,
            (e, ctx) => ValueTask.FromResult((true, projection(e, ctx))));
    }

    public SubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return Select((e, _) => projection(e));
    }

    public SubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, TContext, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Action<TEvent, TContext> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, ValueTask> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Run(Action<TEvent> handler)
        => throw Hooks.HookLowering.NotLowered();

    public SubscriptionPipeline<TEvent, TContext> Use(InstalledKernel kernel)
    {
        kernel.ValidateFor(_adapter);
        _handlerSet.Add(kernel, (e, rawContext, _) => kernel.InvokeAsync(_adapter, e, rawContext.CancellationToken));
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> Use(InstalledKernelPool pool)
    {
        ArgumentNullException.ThrowIfNull(pool);
        pool.ValidateFor(_adapter);
        _handlerSet.Add(pool, (e, rawContext, _) => pool.InvokeAsync(_adapter, e, rawContext.CancellationToken));
        return this;
    }

    public SubscriptionPipeline<TEvent, TContext> Use<TKernel>() where TKernel : class
        => Use(_kernels.GetByKernelType<TKernel>());

    /// <summary>
    /// Wires a lowered <b>local-terminal</b> subscription chain (a remote <c>RunLocal</c> chain): the lowered
    /// <c>Where</c>/<c>Select</c> always run here in the sandbox, and for each event that passes the filter the
    /// projected value is encoded and handed to <paramref name="push"/> for delivery across the IPC boundary to
    /// the plugin's native delegate. Non-matching events never reach <paramref name="push"/>.
    /// </summary>
    public SubscriptionPipeline<TEvent, TContext> UseProjecting(
        InstalledKernel kernel,
        string subscriptionId,
        Hooks.RemoteLocalPush push)
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

        _handlerSet.Add(kernel, (e, rawContext, _) =>
            Hooks.LocalCallbackProjection.PushAsync(kernel, _adapter, e, rawContext, wholeEvent, subscriptionId, push));

        return this;
    }

    internal bool UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => ReferenceEquals(_adapter, adapter);

    internal bool UsesContextFactory(Func<HookContext, TContext> createContext)
        => _contextFactory.Uses(createContext);

    bool ISubscriptionPipeline<TEvent>.UsesAdapter(IPluginEventAdapter<TEvent> adapter)
        => UsesAdapter(adapter);

    void IKernelHandlerPipeline.RemoveKernel(InstalledKernel kernel)
        => _handlerSet.Remove(kernel);

    void IKernelHandlerPipeline.RemoveKernelPool(InstalledKernelPool pool)
        => _handlerSet.Remove(pool);

    internal void Publish(TEvent e, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var filters = _filters;
        var handlers = _handlerSet.Snapshot;
        if (filters.Length == 0 && handlers.Length == 0)
        {
            return;
        }

        var rawContext = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultRawContext;
        var context = _contextFactory.Create(rawContext);
        var onFault = _onFault;
        _ = Task.Run(() =>
            SubscriptionDelivery.PublishSafelyAsync(filters, handlers, e, rawContext, context, onFault).AsTask());
    }

    void ISubscriptionPipeline<TEvent>.Publish(TEvent e, CancellationToken cancellationToken)
        => Publish(e, cancellationToken);
}
