using System.Collections.ObjectModel;

namespace DotBoxD.Plugins.Runtime;

internal static class PluginModelCopy
{
    public static IReadOnlyList<T> List<T>(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new ReadOnlyCollection<T>(values.ToArray());
    }

    public static PluginPackage Package(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new PluginPackage(
            Manifest(package.Manifest),
            Module(package.Module),
            Entrypoints(package.Entrypoints));
    }

    public static PluginManifest Manifest(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new PluginManifest(
            manifest.PluginId,
            manifest.Contract,
            manifest.Mode,
            manifest.Effects,
            manifest.LiveSettings,
            CopySubscriptions(manifest.Subscriptions))
        {
            RequiredCapabilities = manifest.RequiredCapabilities,
            RpcEntrypoint = manifest.RpcEntrypoint
        };
    }

    public static SandboxModule Module(SandboxModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return new SandboxModule(
            module.Id,
            module.Version,
            module.TargetSandboxVersion,
            module.CapabilityRequests,
            module.Functions,
            module.Metadata);
    }

    public static KernelEntrypoints Entrypoints(KernelEntrypoints entrypoints)
    {
        ArgumentNullException.ThrowIfNull(entrypoints);
        return entrypoints with { };
    }

    private static IReadOnlyList<HookSubscriptionManifest> CopySubscriptions(
        IReadOnlyList<HookSubscriptionManifest> subscriptions)
    {
        var copy = new HookSubscriptionManifest[subscriptions.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = subscriptions[i] with
            {
                IndexedPredicates = subscriptions[i].IndexedPredicates
            };
        }

        return copy;
    }
}
