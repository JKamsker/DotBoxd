using DotBoxD.Plugins.Json;

namespace DotBoxD.Plugins.Runtime;

internal static class LocalTerminalIdentity
{
    private const string CallbackSubscriptionIdMetadataKey = "callbackSubscriptionId";

    public static string CreateCallbackSubscriptionId()
        => "callback-" + Guid.NewGuid().ToString("N");

    public static string CreateInstallId()
        => "install-" + Guid.NewGuid().ToString("N");

    public static bool IsLocalTerminal(PluginManifest manifest)
    {
        foreach (var subscription in manifest.Subscriptions)
        {
            if (subscription.LocalTerminal || subscription.ResultLocalTerminal)
            {
                return true;
            }
        }

        return false;
    }

    public static string? CallbackSubscriptionId(PluginPackage package)
        => package.Module.Metadata.TryGetValue(CallbackSubscriptionIdMetadataKey, out var id) &&
           !string.IsNullOrWhiteSpace(id)
            ? id
            : null;

    public static PluginPackage WithCallbackSubscriptionId(PluginPackage package, string callbackSubscriptionId)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrEmpty(callbackSubscriptionId);

        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            [CallbackSubscriptionIdMetadataKey] = callbackSubscriptionId
        };
        return package with
        {
            Module = package.Module with { Metadata = metadata }
        };
    }

    public static bool HasSameReplayPackageIdentity(PluginPackage left, PluginPackage right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return string.Equals(
            PluginPackageJsonSerializer.Export(WithoutCallbackSubscriptionId(left)),
            PluginPackageJsonSerializer.Export(WithoutCallbackSubscriptionId(right)),
            StringComparison.Ordinal);
    }

    private static PluginPackage WithoutCallbackSubscriptionId(PluginPackage package)
    {
        if (!package.Module.Metadata.ContainsKey(CallbackSubscriptionIdMetadataKey))
        {
            return package;
        }

        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal);
        metadata.Remove(CallbackSubscriptionIdMetadataKey);
        return package with
        {
            Module = package.Module with { Metadata = metadata }
        };
    }
}
