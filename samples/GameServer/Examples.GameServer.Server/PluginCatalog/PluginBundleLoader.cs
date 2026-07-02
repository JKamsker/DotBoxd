using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Game.Server.PluginCatalog;

internal static class PluginBundleLoader
{
    public static IReadOnlyList<PluginBundlePart> LoadServerParts(string pluginsRoot)
        => LoadParts(pluginsRoot, "server");

    public static IReadOnlyList<PluginBundlePart> LoadClientParts(string pluginsRoot)
        => LoadParts(pluginsRoot, "client");

    private static IReadOnlyList<PluginBundlePart> LoadParts(string pluginsRoot, string half)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            throw new DirectoryNotFoundException($"Plugin bundle root not found: {pluginsRoot}");
        }

        var parts = new List<PluginBundlePart>();
        foreach (var file in Directory.EnumerateFiles(pluginsRoot, "*.json", SearchOption.AllDirectories).Order())
        {
            var relative = Path.GetRelativePath(pluginsRoot, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Length < 4 || !string.Equals(segments[1], half, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryKind(segments[2], out var kind))
            {
                continue;
            }

            var json = File.ReadAllText(file);
            parts.Add(new PluginBundlePart(
                segments[0],
                kind,
                relative,
                PluginPackageJsonSerializer.Import(json)));
        }

        return parts;
    }

    private static bool TryKind(string segment, out BundlePartKind kind)
    {
        kind = segment switch
        {
            "hooks" => BundlePartKind.Hook,
            "subscriptions" => BundlePartKind.Subscription,
            "extensions" => BundlePartKind.Extension,
            _ => default
        };
        return segment is "hooks" or "subscriptions" or "extensions";
    }
}
