using DotBoxD.Plugins;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Client-side hook registration surface for a remote plugin server. The fluent stages are lowered by
/// the analyzer; the terminal installs the generated package through the supplied control-plane callback.
/// </summary>
public sealed class RemoteHookRegistry
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly Func<RemoteLocalCallbackRegistration, ValueTask<string>>? _installLocalCallback;

    public RemoteHookRegistry(
        Func<PluginPackage, ValueTask<string>> install,
        Func<RemoteLocalCallbackRegistration, ValueTask<string>>? installLocalCallback = null)
    {
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _installLocalCallback = installLocalCallback;
    }

    public RemoteHookPipeline<TEvent> On<TEvent>() => new(_install, _installLocalCallback);
}
