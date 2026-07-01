using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

[PipelineSurface(PipelineTransport.Remote)]
public sealed partial class RemoteHookPipeline<TEvent, TContext>
{
    private readonly RemoteHookPipeline<TEvent> _inner;
    private readonly Func<HookContext, TContext> _createContext;

    internal RemoteHookPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        Func<HookContext, TContext> createContext,
        RemoteLocalHandlerRegistry? localHandlers = null)
    {
        _inner = new RemoteHookPipeline<TEvent>(install, localHandlers);
        _createContext = createContext;
    }

    public RemoteHookPipeline<TEvent, TContext> Use<TKernel>() where TKernel : class
        => UseGeneratedChain(KernelPackageRegistry.Resolve<TKernel>());

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        _inner.UseGeneratedChain(package);
        return this;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteHookPipeline<TEvent, TContext> Where(Func<TEvent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteHookPipeline<TEvent, TContext> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(this);
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteHookStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext, TContext>(this);
    }

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteHookPipeline<TEvent, TContext> Run(Func<TEvent, TContext, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteHookPipeline<TEvent, TContext> Run(Action<TEvent, TContext> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteHookPipeline<TEvent, TContext> Run(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteHookPipeline<TEvent, TContext> Run(Action<TEvent> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteHookPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
        => throw LocalHandlersNotSupported();

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler)
        => InstallLocal(package, handler);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e));
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(PluginPackage package, Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent, TContext> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, ctx) =>
        {
            handler(e, ctx);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteHookPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        }, decoder);
    }

    internal RemoteHookPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler)
    {
        _inner.InstallLocal<TProjected>(package, (value, rawContext) => handler(value, _createContext(rawContext)));
        return this;
    }

    internal RemoteHookPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler,
        Func<KernelRpcValue, TProjected> decoder)
    {
        _inner.InstallLocal(package, (value, rawContext) => handler(value, _createContext(rawContext)), decoder);
        return this;
    }

    internal RemoteHookPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TProjected> decoder)
    {
        _inner.InstallLocal(package, (value, rawContext) => handler(value, _createContext(rawContext)), decoder);
        return this;
    }

    private static InvalidOperationException NotLowered()
        => new("Remote hook Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote hook RunLocal requires an event callback transport; use PluginServer.Hooks for local handlers.");

}
