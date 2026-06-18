using DotBoxD.Plugins;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Client-side fire-and-forget subscription registration surface for a remote plugin server.
/// </summary>
public sealed class RemoteSubscriptionRegistry
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;
    private readonly Func<RemoteLocalCallbackRegistration, ValueTask<string>>? _installLocalCallback;

    public RemoteSubscriptionRegistry(
        Func<PluginPackage, ValueTask<string>> install,
        Func<RemoteLocalCallbackRegistration, ValueTask<string>>? installLocalCallback = null)
    {
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _installLocalCallback = installLocalCallback;
    }

    public RemoteSubscriptionPipeline<TEvent> On<TEvent>() => new(_install, _installLocalCallback);
}
