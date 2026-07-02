namespace DotBoxD.Kernels.Game.Client.Plugins;

internal sealed record ClientBundlePart(
    string BundleId,
    ClientBundlePartKind Kind,
    string RelativePath,
    PluginPackage Package);

internal enum ClientBundlePartKind
{
    Hook,
    Subscription,
    Extension
}
