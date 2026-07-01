namespace DotBoxD.Plugins.Runtime.Subscriptions;

[PipelineSurface(PipelineTransport.Remote)]
public sealed class RemoteSubscriptionStage<TEvent, TCurrent>
{
    private readonly RemoteSubscriptionPipeline<TEvent> _root;

    internal RemoteSubscriptionStage(RemoteSubscriptionPipeline<TEvent> root)
        => _root = root;

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteSubscriptionStage<TEvent, TCurrent> Where(Func<TCurrent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteSubscriptionStage<TEvent, TCurrent> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TCurrent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(_root);
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteSubscriptionStage<TEvent, TNext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext>(_root);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    /// <summary>
    /// Installs a lowered <c>RunLocal</c> subscription chain whose projected type is
    /// <typeparamref name="TCurrent"/> (produced by the preceding <c>Select</c>). The filter+projection
    /// installs server-side; the native delegate receives the projected value pushed back per matching event.
    /// </summary>
    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, HookContext, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal(package, handler);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent, HookContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value));
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });
    }

    // Decoder overloads: a projection RunLocal subscription whose projected type TCurrent is wire-eligible
    // installs with the generated reflection-free decoder, emitted by the interceptor as the 3rd argument.
    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, HookContext, ValueTask> handler, Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal(package, handler, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent, HookContext> handler, Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, ValueTask> handler, Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler, Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, HookContext, ValueTask> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal(package, handler, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent, HookContext> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Func<TCurrent, ValueTask> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler, Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent> Run(Func<TCurrent, HookContext, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent> Run(Action<TCurrent, HookContext> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent> Run(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent> Run(Action<TCurrent> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TCurrent, HookContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TCurrent, HookContext> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent> RunLocal(Func<TCurrent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent> RunLocal(Action<TCurrent> handler)
        => throw LocalHandlersNotSupported();

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");
}
