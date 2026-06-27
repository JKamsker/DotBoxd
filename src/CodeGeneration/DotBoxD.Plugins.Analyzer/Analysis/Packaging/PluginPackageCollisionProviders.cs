using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginPackageCollisionProviders
{
    public static IncrementalValueProvider<EquatableArray<GeneratedPluginPackageIdentity>> RegisterBlockedIdentities(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<GeneratedPluginPackageIdentity>> pluginPackageIdentities,
        IncrementalValueProvider<ImmutableArray<GeneratedPluginPackageIdentity>> eventKernelPackageIdentities,
        IncrementalValueProvider<ImmutableArray<GeneratedPluginPackageIdentity>> chainPackageIdentities,
        IncrementalValueProvider<ImmutableArray<GeneratedPluginPackageIdentity>> rpcPackageIdentities)
    {
        var packageIdentityGroups = pluginPackageIdentities
            .Combine(eventKernelPackageIdentities)
            .Combine(chainPackageIdentities)
            .Combine(rpcPackageIdentities);
        var duplicateIdentities = GeneratorGuard.TransformValueOrDefault(
            context,
            packageIdentityGroups,
            "plugin package duplicate detection",
            static (pair, _) => PluginPackageDuplicateDetector.FindDuplicates(
                pair.Left.Left.Left,
                pair.Left.Left.Right,
                pair.Left.Right,
                pair.Right));
        RegisterDiagnostics(context, duplicateIdentities, PluginPackageDuplicateDetector.Diagnostics, "duplicate");

        var sourceCollisionIdentities = GeneratorGuard.TransformValueOrDefault(
            context,
            PluginPackageSourceTypeCollector.Collect(context).Combine(packageIdentityGroups),
            "plugin package source collision detection",
            static (pair, _) => PluginPackageDuplicateDetector.FindSourceCollisions(
                pair.Left,
                pair.Right.Left.Left.Left,
                pair.Right.Left.Left.Right,
                pair.Right.Left.Right,
                pair.Right.Right));
        RegisterDiagnostics(
            context,
            sourceCollisionIdentities,
            PluginPackageDuplicateDetector.SourceCollisionDiagnostics,
            "source collision");

        return GeneratorGuard.TransformValueOrDefault(
            context,
            duplicateIdentities.Combine(sourceCollisionIdentities),
            "plugin package blocked identity merge",
            static (pair, _) => PluginPackageDuplicateDetector.Merge(pair.Left, pair.Right));
    }

    private static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<EquatableArray<GeneratedPluginPackageIdentity>> identities,
        Func<EquatableArray<GeneratedPluginPackageIdentity>, IEnumerable<GeneratedPluginPackageDiagnostic>> diagnostics,
        string kind)
        => GeneratorGuard.RegisterOutput(
            context,
            identities.SelectMany((items, _) => diagnostics(items)),
            "plugin package " + kind + " diagnostic output",
            static (context, diagnostic) => context.ReportDiagnostic(Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                Location.None,
                diagnostic.Message)));
}
