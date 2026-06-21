using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Client-side hook registration surface for a remote plugin server. The fluent stages are lowered by
/// the analyzer; a <c>Run</c> terminal installs the generated verified-IR package through the supplied
/// control-plane callback, while a <c>RunLocal</c> terminal additionally registers its native delegate in
/// the supplied local-handler registry so the server can push filtered+projected values back to it.
/// </summary>
public sealed class RemoteHookRegistry
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly RemoteLocalHandlerRegistry? _localHandlers;

    public RemoteHookRegistry(
        Func<PluginPackage, ValueTask<string>> install,
        RemoteLocalHandlerRegistry? localHandlers = null)
    {
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _localHandlers = localHandlers;
    }

    public RemoteHookPipeline<TEvent> On<TEvent>() => new(_install, _localHandlers);
}
