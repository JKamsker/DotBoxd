using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Client-side fire-and-forget subscription registration surface for a remote plugin server. A
/// <c>RunLocal</c> terminal registers its native delegate in the supplied local-handler registry so the
/// server can push filtered+projected values back to it per matching event.
/// </summary>
public sealed class RemoteSubscriptionRegistry
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly RemoteLocalHandlerRegistry? _localHandlers;

    public RemoteSubscriptionRegistry(
        Func<PluginPackage, ValueTask<string>> install,
        RemoteLocalHandlerRegistry? localHandlers = null)
    {
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _localHandlers = localHandlers;
    }

    [PipelineStep(PipelineStepRole.Seed)]
    public RemoteSubscriptionPipeline<TEvent> On<TEvent>() => new(_install, _localHandlers);

    [PipelineStep(PipelineStepRole.Seed)]
    public RemoteSubscriptionPipeline<TEvent, TContext> On<TEvent, TContext>(
        Func<HookContext, TContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        return new RemoteSubscriptionPipeline<TEvent, TContext>(_install, createContext, _localHandlers);
    }
}
