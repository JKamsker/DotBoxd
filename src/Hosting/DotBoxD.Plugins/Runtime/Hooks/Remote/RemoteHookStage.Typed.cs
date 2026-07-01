namespace DotBoxD.Plugins.Runtime.Hooks;

[PipelineSurface(PipelineTransport.Remote)]
public sealed partial class RemoteHookStage<TEvent, TCurrent, TContext>
{
    private readonly RemoteHookPipeline<TEvent, TContext> _root;

    internal RemoteHookStage(RemoteHookPipeline<TEvent, TContext> root)
        => _root = root;

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteHookStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteHookStage<TEvent, TCurrent, TContext> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(_root);
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(_root);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler)
        => _root.InstallLocal(package, handler);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value));
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(PluginPackage package, Action<TCurrent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) =>
        {
            handler(value);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<KernelRpcValue, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
        => _root.InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TCurrent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return _root.InstallLocal<TCurrent>(package, (value, _) => handler(value), decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
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
    public RemoteHookPipeline<TEvent, TContext> Run(Func<TCurrent, TContext, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteHookPipeline<TEvent, TContext> Run(Action<TCurrent, TContext> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteHookPipeline<TEvent, TContext> Run(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteHookPipeline<TEvent, TContext> Run(Action<TCurrent> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Func<TCurrent, TContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Action<TCurrent, TContext> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Func<TCurrent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Action<TCurrent> handler)
        => throw LocalHandlersNotSupported();

    private static InvalidOperationException NotLowered()
        => new("Remote hook Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote hook RunLocal requires an event callback transport; use PluginServer.Hooks for local handlers.");
}
