using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginPackageGeneratorOutput
{
    public static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<PluginKernelModelResult> results)
        => GeneratorGuard.RegisterOutput(
            context,
            results
                .Where(static result => result.Diagnostic is not null)
                .Select(static (result, _) => result.Diagnostic!)
                .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.DiagnosticResult),
            "plugin diagnostic output",
            static (context, diagnostic) => context.ReportDiagnostic(diagnostic.ToDiagnostic()));

    public static void RegisterPackageSources(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<GeneratedPluginPackage> packages,
        IncrementalValueProvider<EquatableArray<GeneratedPluginPackageIdentity>> duplicateIdentities)
        => GeneratorGuard.RegisterOutput(
            context,
            packages
                .Combine(duplicateIdentities)
                .Where(static pair => !PluginPackageDuplicateDetector.Contains(pair.Right, pair.Left))
                .Select(static (pair, _) => pair.Left),
            "plugin package source output",
            static (context, package) => context.AddSource(package.HintName, package.Source));
}
