using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Kernels.Game.Shared;

internal static class PackageExporter
{
    public static void Export(Type kernelType, string pluginsRoot, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(kernelType);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var package = KernelPackageRegistry.Resolve(kernelType);
        var path = Path.GetFullPath(Path.Combine(pluginsRoot, relativePath));
        var directory = Path.GetDirectoryName(path) ??
            throw new InvalidOperationException("Package path has no directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, PluginPackageJsonSerializer.Export(package, indented: true));
    }
}
