namespace SafeIR.PluginAnalyzer;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[Generator(LanguageNames.CSharp)]
public sealed class SafeIrPluginPackageGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var modelResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SafeIrGenerationNames.Metadata.GamePluginAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        var diagnostics = modelResults
            .Where(static result => result.Diagnostic is not null)
            .Select(static (result, _) => result.Diagnostic!)
            .WithTrackingName(SafeIrPluginPackageGeneratorTrackingNames.DiagnosticResult);
        context.RegisterSourceOutput(diagnostics, static (context, diagnostic) =>
            context.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var models = modelResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(SafeIrPluginPackageGeneratorTrackingNames.ModelResult);

        var packages = models
            .Collect()
            .Select(static (models, _) => CreatePackageBatch(models))
            .WithTrackingName(SafeIrPluginPackageGeneratorTrackingNames.PackageResult);

        context.RegisterSourceOutput(packages, static (context, batch) => {
            foreach (var diagnostic in batch.Diagnostics)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                    Location.None,
                    diagnostic.Message));
            }

            foreach (var package in batch.Packages)
            {
                context.AddSource(package.HintName, package.Source);
            }
        });
    }

    private static GeneratedPluginPackageBatch CreatePackageBatch(ImmutableArray<PluginKernelModel> models)
    {
        var duplicateIdentities = models
            .GroupBy(PackageIdentity, StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .ToDictionary(group => group.Key, StringComparer.Ordinal);
        var diagnostics = duplicateIdentities.Values.Select(group => new GeneratedPluginPackageDiagnostic(
            $"Plugin package name '{group.First().PackageName}' is generated more than once in namespace '{NamespaceDisplay(group.First())}'."));
        var packages = models
            .Where(model => !duplicateIdentities.ContainsKey(PackageIdentity(model)))
            .Select(SafeIrPackageSourceEmitter.Emit);

        return new GeneratedPluginPackageBatch(
            new EquatableArray<GeneratedPluginPackage>(packages),
            new EquatableArray<GeneratedPluginPackageDiagnostic>(diagnostics));
    }

    private static string PackageIdentity(PluginKernelModel model)
        => model.Namespace + "\0" + model.PackageName;

    private static string NamespaceDisplay(PluginKernelModel model)
        => string.IsNullOrWhiteSpace(model.Namespace) ? "<global>" : model.Namespace;
}
