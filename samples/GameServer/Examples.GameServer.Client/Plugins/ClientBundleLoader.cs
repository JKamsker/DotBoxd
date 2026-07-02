using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Game.Client.Plugins;

internal static class ClientBundleLoader
{
    public static IReadOnlyList<ClientBundlePart> Load(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            throw new DirectoryNotFoundException($"Plugin bundle root not found: {pluginsRoot}");
        }

        var parts = new List<ClientBundlePart>();
        foreach (var file in Directory.EnumerateFiles(pluginsRoot, "*.json", SearchOption.AllDirectories).Order())
        {
            var relative = Path.GetRelativePath(pluginsRoot, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Length < 4 || !string.Equals(segments[1], "client", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryKind(segments[2], out var kind))
            {
                continue;
            }

            parts.Add(new ClientBundlePart(
                segments[0],
                kind,
                relative,
                PluginPackageJsonSerializer.Import(File.ReadAllText(file))));
        }

        return parts;
    }

    private static bool TryKind(string segment, out ClientBundlePartKind kind)
    {
        kind = segment switch
        {
            "hooks" => ClientBundlePartKind.Hook,
            "subscriptions" => ClientBundlePartKind.Subscription,
            "extensions" => ClientBundlePartKind.Extension,
            _ => default
        };
        return segment is "hooks" or "subscriptions" or "extensions";
    }
}
