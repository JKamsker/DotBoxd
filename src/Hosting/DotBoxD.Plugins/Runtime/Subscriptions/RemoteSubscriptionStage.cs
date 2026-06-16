using DotBoxD.Plugins;

namespace DotBoxD.Plugins.Runtime.Subscriptions;

public sealed class RemoteSubscriptionStage<TEvent, TCurrent>
{
    private readonly RemoteSubscriptionPipeline<TEvent> _root;

    internal RemoteSubscriptionStage(RemoteSubscriptionPipeline<TEvent> root)
        => _root = root;

    public RemoteSubscriptionStage<TEvent, TCurrent> Where(Func<TCurrent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteSubscriptionStage<TEvent, TCurrent> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TCurrent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(_root);
    }

    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(_root);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    public RemoteSubscriptionPipeline<TEvent> Run(Func<TCurrent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Action<TCurrent, HookContext> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> Run(Action<TCurrent> handler)
        => throw NotLowered();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TCurrent, HookContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TCurrent, HookContext> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TCurrent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TCurrent> handler)
        => throw LocalHandlersNotSupported();

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");
}
