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

    public static EquatableArray<GeneratedPluginPackageIdentity> FindSourceCollisions(
        ImmutableArray<GeneratedPluginPackageIdentity> sourceTypes,
        ImmutableArray<GeneratedPluginPackageIdentity> pluginPackages,
        ImmutableArray<GeneratedPluginPackageIdentity> eventKernelPackages,
        ImmutableArray<GeneratedPluginPackageIdentity> chainPackages,
        ImmutableArray<GeneratedPluginPackageIdentity> rpcPackages)
    {
        var sourceTypeSet = new HashSet<GeneratedPluginPackageIdentity>(sourceTypes);
        var collisions = new List<GeneratedPluginPackageIdentity>();
        AddSourceCollisions(collisions, sourceTypeSet, pluginPackages);
        AddSourceCollisions(collisions, sourceTypeSet, eventKernelPackages);
        AddSourceCollisions(collisions, sourceTypeSet, chainPackages);
        AddSourceCollisions(collisions, sourceTypeSet, rpcPackages);

        return new EquatableArray<GeneratedPluginPackageIdentity>(collisions);
    }

    public static EquatableArray<GeneratedPluginPackageIdentity> Merge(
        EquatableArray<GeneratedPluginPackageIdentity> first,
        EquatableArray<GeneratedPluginPackageIdentity> second)
    {
        var blocked = new List<GeneratedPluginPackageIdentity>(first.Count + second.Count);
        AddUnique(blocked, first);
        AddUnique(blocked, second);
        return new EquatableArray<GeneratedPluginPackageIdentity>(blocked);
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

    public static IEnumerable<GeneratedPluginPackageDiagnostic> SourceCollisionDiagnostics(
        EquatableArray<GeneratedPluginPackageIdentity> collisions)
    {
        foreach (var collision in collisions)
        {
            yield return new GeneratedPluginPackageDiagnostic(
                $"Plugin package name '{collision.PackageName}' collides with an existing type in namespace '{collision.NamespaceDisplay}'.");
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

    private static void AddSourceCollisions(
        List<GeneratedPluginPackageIdentity> collisions,
        HashSet<GeneratedPluginPackageIdentity> sourceTypes,
        ImmutableArray<GeneratedPluginPackageIdentity> packages)
    {
        foreach (var package in packages)
        {
            if (sourceTypes.Contains(package) && !collisions.Contains(package))
            {
                collisions.Add(package);
            }
        }
    }

    private static void AddUnique(
        List<GeneratedPluginPackageIdentity> target,
        EquatableArray<GeneratedPluginPackageIdentity> source)
    {
        foreach (var item in source)
        {
            if (!target.Contains(item))
            {
                target.Add(item);
            }
        }
    }
}
