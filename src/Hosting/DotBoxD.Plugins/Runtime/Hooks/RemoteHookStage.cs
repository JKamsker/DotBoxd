using DotBoxD.Plugins;

namespace DotBoxD.Plugins.Runtime.Hooks;

public sealed class RemoteHookStage<TEvent, TCurrent>
{
    private readonly RemoteHookPipeline<TEvent> _root;

    internal RemoteHookStage(RemoteHookPipeline<TEvent> root)
        => _root = root;

    public RemoteHookStage<TEvent, TCurrent> Where(Func<TCurrent, HookContext, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteHookStage<TEvent, TCurrent> Where(Func<TCurrent, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return this;
    }

    public RemoteHookStage<TEvent, TNext> Select<TNext>(Func<TCurrent, HookContext, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(_root);
    }

    public RemoteHookStage<TEvent, TNext> Select<TNext>(Func<TCurrent, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new RemoteHookStage<TEvent, TNext>(_root);
    }

    public RemoteHookPipeline<TEvent> UseGeneratedChain(PluginPackage package)
        => _root.UseGeneratedChain(package);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain(
        PluginPackage package,
        Func<TCurrent, HookContext, ValueTask> handler)
        => _root.UseGeneratedLocalCallbackChain(package, handler);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain(
        PluginPackage package,
        Action<TCurrent, HookContext> handler)
        => _root.UseGeneratedLocalCallbackChain(package, handler);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain(
        PluginPackage package,
        Func<TCurrent, ValueTask> handler)
        => _root.UseGeneratedLocalCallbackChain(package, handler);

    public RemoteHookPipeline<TEvent> UseGeneratedLocalCallbackChain(
        PluginPackage package,
        Action<TCurrent> handler)
        => _root.UseGeneratedLocalCallbackChain(package, handler);

    public RemoteHookPipeline<TEvent> Run(Func<TCurrent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Action<TCurrent, HookContext> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> Run(Action<TCurrent> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Func<TCurrent, HookContext, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Action<TCurrent, HookContext> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Func<TCurrent, ValueTask> handler)
        => throw NotLowered();

    public RemoteHookPipeline<TEvent> RunLocal(Action<TCurrent> handler)
        => throw NotLowered();

    private static InvalidOperationException NotLowered()
        => new("Remote hook Run/RunLocal lambda calls must be intercepted by the DotBoxD plugin generator.");
}
