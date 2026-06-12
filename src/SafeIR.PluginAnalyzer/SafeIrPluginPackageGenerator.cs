namespace SafeIR.PluginAnalyzer;

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
            .Where(static result => result.Diagnostic is not null);
        context.RegisterSourceOutput(diagnostics, static (context, result) =>
            context.ReportDiagnostic(result.Diagnostic!));

        var models = modelResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(SafeIrPluginPackageGeneratorTrackingNames.ModelResult);

        var packages = models
            .Select(static (model, _) => SafeIrPackageSourceEmitter.Emit(model))
            .WithTrackingName(SafeIrPluginPackageGeneratorTrackingNames.PackageResult);

        context.RegisterSourceOutput(packages, static (context, package) => {
            context.AddSource(package.HintName, package.Source);
        });
    }
}
