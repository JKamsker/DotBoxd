using System.Collections.Immutable;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginPackageDuplicateDetector
{
    public static EquatableArray<GeneratedPluginPackageIdentity> FindDuplicates(
        ImmutableArray<GeneratedPluginPackageIdentity> pluginPackages,
        ImmutableArray<GeneratedPluginPackageIdentity> eventKernelPackages,
        ImmutableArray<GeneratedPluginPackageIdentity> chainPackages,
        ImmutableArray<GeneratedPluginPackageIdentity> rpcPackages)
    {
        var counts = new Dictionary<GeneratedPluginPackageIdentity, int>();
        CountPackages(counts, pluginPackages);
        CountPackages(counts, eventKernelPackages);
        CountPackages(counts, chainPackages);
        CountPackages(counts, rpcPackages);

        var duplicates = new List<GeneratedPluginPackageIdentity>();
        foreach (var pair in counts)
        {
            if (pair.Value > 1)
            {
                duplicates.Add(pair.Key);
            }
        }

        return new EquatableArray<GeneratedPluginPackageIdentity>(duplicates);
    }

    public static IEnumerable<GeneratedPluginPackageDiagnostic> Diagnostics(
        EquatableArray<GeneratedPluginPackageIdentity> duplicates)
    {
        foreach (var duplicate in duplicates)
        {
            yield return new GeneratedPluginPackageDiagnostic(
                $"Plugin package name '{duplicate.PackageName}' is generated more than once in namespace '{duplicate.NamespaceDisplay}'.");
        }
    }

    public static bool Contains(
        EquatableArray<GeneratedPluginPackageIdentity> duplicates,
        GeneratedPluginPackage package)
    {
        var identity = GeneratedPluginPackageIdentity.From(package);
        foreach (var duplicate in duplicates)
        {
            if (duplicate.Equals(identity))
            {
                return true;
            }
        }

        return false;
    }

    private static void CountPackages(
        Dictionary<GeneratedPluginPackageIdentity, int> counts,
        ImmutableArray<GeneratedPluginPackageIdentity> packages)
    {
        foreach (var package in packages)
        {
            counts.TryGetValue(package, out var count);
            counts[package] = count + 1;
        }
    }
}
