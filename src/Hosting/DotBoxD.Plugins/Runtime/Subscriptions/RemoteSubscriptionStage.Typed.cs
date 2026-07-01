namespace DotBoxD.Plugins.Runtime.Subscriptions;

[PipelineSurface(PipelineTransport.Remote)]
public sealed class RemoteSubscriptionStage<TEvent, TCurrent, TContext>
{
    private readonly RemoteSubscriptionPipeline<TEvent, TContext> _root;

    internal RemoteSubscriptionStage(RemoteSubscriptionPipeline<TEvent, TContext> root)
        => _root = root;

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteSubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteSubscriptionStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TCurrent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext, TContext>(_root);
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext, TContext>(_root);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler)
        => _root.InstallLocal(package, handler);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value));
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent, TContext> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, ctx) =>
        {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TCurrent> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, TContext, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent, TContext> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TCurrent> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent, TContext> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TCurrent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
        => throw LocalHandlersNotSupported();

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");
}
