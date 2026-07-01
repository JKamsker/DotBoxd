using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Plugins.Runtime;

[PipelineSurface(PipelineTransport.Remote)]
public sealed class RemoteSubscriptionPipeline<TEvent, TContext>
{
    private readonly RemoteSubscriptionPipeline<TEvent> _inner;
    private readonly Func<HookContext, TContext> _createContext;

    internal RemoteSubscriptionPipeline(
        Func<PluginPackage, ValueTask<string>> install,
        Func<HookContext, TContext> createContext,
        RemoteLocalHandlerRegistry? localHandlers = null)
    {
        _inner = new RemoteSubscriptionPipeline<TEvent>(install, localHandlers);
        _createContext = createContext;
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> Use<TKernel>() where TKernel : class
        => UseGeneratedChain(KernelPackageRegistry.Resolve<TKernel>());

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedChain(PluginPackage package)
    {
        _inner.UseGeneratedChain(package);
        return this;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, TContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Filter)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Where(Func<TEvent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(
        Func<TEvent, TContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext, TContext>(this);
    }

    [PipelineStep(PipelineStepRole.Projection)]
    public RemoteSubscriptionStage<TEvent, TNext, TContext> Select<TNext>(Func<TEvent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteSubscriptionStage<TEvent, TNext, TContext>(this);
    }

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, TContext, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TEvent, TContext> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Func<TEvent, ValueTask> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.Run)]
    public RemoteSubscriptionPipeline<TEvent, TContext> Run(Action<TEvent> handler)
        => throw NotLowered();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, TContext, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent, TContext> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Func<TEvent, ValueTask> handler)
        => throw LocalHandlersNotSupported();

    [PipelineStep(PipelineStepRole.RunLocal)]
    public RemoteSubscriptionPipeline<TEvent, TContext> RunLocal(Action<TEvent> handler)
        => throw LocalHandlersNotSupported();

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler)
        => InstallLocal(package, handler);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e));
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) =>
        {
            handler(e);
            return ValueTask.CompletedTask;
        });
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<KernelRpcValue, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
        => InstallLocal(package, handler, decoder);

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
        PluginPackage package,
        Func<TEvent, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TEvent> decoder)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return InstallLocal<TEvent>(package, (e, _) => handler(e), decoder);
    }

    public RemoteSubscriptionPipeline<TEvent, TContext> UseGeneratedLocalChain(
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

    internal RemoteSubscriptionPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler)
    {
        // Guard here at the single capture site so every UseGeneratedLocalChain entry — including the overloads
        // that forward straight through — fails fast at registration instead of NREing during a later callback.
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        _inner.InstallLocal<TProjected>(package, (value, rawContext) => handler(value, _createContext(rawContext)));
        return this;
    }

    internal RemoteSubscriptionPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler,
        Func<KernelRpcValue, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);
        _inner.InstallLocal(package, (value, rawContext) => handler(value, _createContext(rawContext)), decoder);
        return this;
    }

    internal RemoteSubscriptionPipeline<TEvent, TContext> InstallLocal<TProjected>(
        PluginPackage package,
        Func<TProjected, TContext, ValueTask> handler,
        Func<ReadOnlyMemory<byte>, TProjected> decoder)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(decoder);
        _inner.InstallLocal(package, (value, rawContext) => handler(value, _createContext(rawContext)), decoder);
        return this;
    }

    private static InvalidOperationException NotLowered()
        => new("Remote subscription Run(lambda) calls must be intercepted by the DotBoxD plugin generator.");

    private static NotSupportedException LocalHandlersNotSupported()
        => new("Remote subscription RunLocal requires an event callback transport; use PluginServer.Subscriptions for local handlers.");
}
