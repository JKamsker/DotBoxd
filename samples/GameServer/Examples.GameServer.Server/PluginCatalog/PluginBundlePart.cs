namespace DotBoxD.Kernels.Game.Server.PluginCatalog;

internal sealed record PluginBundlePart(
    string BundleId,
    BundlePartKind Kind,
    string RelativePath,
    PluginPackage Package);
