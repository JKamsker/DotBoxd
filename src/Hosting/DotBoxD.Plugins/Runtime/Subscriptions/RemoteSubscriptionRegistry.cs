using DotBoxD.Plugins;

namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Client-side fire-and-forget subscription registration surface for a remote plugin server.
/// </summary>
public sealed class RemoteSubscriptionRegistry
{
    private readonly Func<PluginPackage, ValueTask<string>> _install;

    public RemoteSubscriptionRegistry(Func<PluginPackage, ValueTask<string>> install)
        => _install = install ?? throw new ArgumentNullException(nameof(install));

    public RemoteSubscriptionPipeline<TEvent> On<TEvent>() => new(_install);
}
