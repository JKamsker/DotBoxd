using DotBoxD.Plugins.Analyzer.Analysis.HookChains;
using DotBoxD.Plugins.Analyzer.Analysis.HookResults;
using DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.PluginServer;
using DotBoxD.Plugins.Analyzer.Analysis.Registration;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

[Generator(LanguageNames.CSharp)]
public sealed class PluginPackageGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var pluginAttributeResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDMetadataNames.PluginAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        var eventKernelAttributeResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDMetadataNames.EventKernelAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        RegisterDiagnostics(context, pluginAttributeResults);
        RegisterDiagnostics(context, eventKernelAttributeResults);

        var pluginModels = pluginAttributeResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.ModelResult);
        var eventKernelModels = eventKernelAttributeResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.ModelResult);

        var pluginPackages = pluginModels
            .Select(static (model, _) => DotBoxDPackageSourceEmitter.Emit(model))
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);
        var eventKernelPackages = eventKernelModels
            .Select(static (model, _) => DotBoxDPackageSourceEmitter.Emit(model))
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);

        var pluginPackageIdentities = pluginPackages
            .Select(static (package, _) => GeneratedPluginPackageIdentity.From(package))
            .Collect();
        var eventKernelPackageIdentities = eventKernelPackages
            .Select(static (package, _) => GeneratedPluginPackageIdentity.From(package))
            .Collect();

        // Phase C: lower inline On<TEvent>().Where?(lambda).Select?(lambda).Run(lambda) chains
        // to the same PluginKernelModel a kernel class produces. Unsupported shapes fail safe (no package);
        // a recognized remote RunLocal chain that fails to lower also carries a DBXK111 diagnostic.
        var chainCreateResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsHookChainTerminal(node),
                static (syntaxContext, ct) => HookChainModelFactory.Create(syntaxContext, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        var remoteStagedUseDiagnostics = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => RemoteStagedUseDiagnosticFactory.IsCandidate(node),
                static (syntaxContext, ct) => RemoteStagedUseDiagnosticFactory.Create(syntaxContext, ct))
            .Where(static diagnostic => diagnostic is not null)
            .Select(static (diagnostic, _) => diagnostic!);
        context.RegisterSourceOutput(
            remoteStagedUseDiagnostics,
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        // Report DBXK111 for a recognized remote RunLocal chain whose stages could not be lowered, so the
        // otherwise-silent skip (and the runtime NotSupportedException it leads to) is visible at build time.
        context.RegisterSourceOutput(
            chainCreateResults
                .Where(static result => result.Diagnostic is not null)
                .Select(static (result, _) => result.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var chainResults = chainCreateResults
            .Where(static result => result.Chain is not null)
            .Select(static (result, _) => result.Chain!);

        // Server extensions: lower a [ServerExtension] class's batch method to a verified-IR package
        // whose Create() imports the JSON. Unsupported shapes emit a diagnostic and no package.
        var rpcResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDMetadataNames.ServerExtensionAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => RpcKernelModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);

        context.RegisterSourceOutput(
            rpcResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var rpcPackages = rpcResults
            .Where(static result => result.Package is not null)
            .Select(static (result, _) => result.Package!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);

        var invokeAsyncResults = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "InvokeAsync" }
                },
                static (syntaxContext, ct) => InvokeAsyncModelFactory.Create(syntaxContext, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        context.RegisterSourceOutput(
            invokeAsyncResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        context.RegisterSourceOutput(
            invokeAsyncResults.Where(static result => result.Package is not null).Select(static (result, _) => result.Package!),
            static (sourceContext, package) => sourceContext.AddSource(package.HintName, package.Source));

        var pluginServerResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDMetadataNames.GeneratePluginServerAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => PluginServerFacadeModelFactory.Create(ctx, ct))
            .Where(static result => result is not null)
            .Select(static (result, _) => result!);
        context.RegisterSourceOutput(
            pluginServerResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        context.RegisterSourceOutput(
            pluginServerResults.Where(static result => result.Source is not null).Select(static (result, _) => result.Source!),
            static (sourceContext, source) => sourceContext.AddSource(source.HintName, source.Source));

        var chainPackages = chainResults
            .Select(static (result, _) => DotBoxDPackageSourceEmitter.Emit(result.Model))
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);
        var chainPackageIdentities = chainPackages
            .Select(static (package, _) => GeneratedPluginPackageIdentity.From(package))
            .Collect();
        var rpcPackageIdentities = rpcPackages
            .Select(static (package, _) => GeneratedPluginPackageIdentity.From(package))
            .Collect();
        var rpcGraftCollisions = rpcResults
            .Collect()
            .Select(static (results, _) => RpcKernelGraftCollisionDetector.FindDuplicates(results));
        context.RegisterSourceOutput(
            rpcGraftCollisions.SelectMany(static (collisions, _) => RpcKernelGraftCollisionDetector.Diagnostics(collisions)),
            static (context, diagnostic) => context.ReportDiagnostic(diagnostic));
        var duplicateIdentities = pluginPackageIdentities
            .Combine(eventKernelPackageIdentities)
            .Combine(chainPackageIdentities)
            .Combine(rpcPackageIdentities)
            .Select(static (pair, _) => PluginPackageDuplicateDetector.FindDuplicates(
                pair.Left.Left.Left,
                pair.Left.Left.Right,
                pair.Left.Right,
                pair.Right));
        context.RegisterSourceOutput(
            duplicateIdentities.SelectMany(static (duplicates, _) => PluginPackageDuplicateDetector.Diagnostics(duplicates)),
            static (context, diagnostic) => context.ReportDiagnostic(Diagnostic.Create(
                PluginAnalyzerDiagnostics.UnsupportedKernelShapeRule,
                Location.None,
                diagnostic.Message)));
        RegisterPackageSources(context, pluginPackages, duplicateIdentities);
        RegisterPackageSources(context, eventKernelPackages, duplicateIdentities);
        RegisterPackageSources(context, chainPackages, duplicateIdentities);
        RegisterPackageSources(context, rpcPackages, duplicateIdentities);

        // Emit a C# interceptor per lowered chain so the Run call site installs + wires its
        // generated package (UseGeneratedChain) instead of throwing DBXK062.
        var interceptions = chainResults
            .Where(static result => result.Interception is not null)
            .Select(static (result, _) => result.Interception!)
            .Collect();
        var invokeAsyncInterceptions = invokeAsyncResults
            .Where(static result => result.Interception is not null)
            .Select(static (result, _) => result.Interception!)
            .Collect();
        context.RegisterSourceOutput(
            interceptions,
            static (sourceContext, items) => DotBoxDHookChainInterceptorEmitter.Emit(sourceContext, items));
        context.RegisterSourceOutput(
            invokeAsyncInterceptions,
            static (sourceContext, items) => InvokeAsyncInterceptorEmitter.Emit(sourceContext, items));

        RegistrationAccumulatorGenerator.Register(context);

        // [HookResult] builder surface: Ok()/Reject()/With<Field>() for each annotated result record, plus the
        // DBXK112 diagnostic when the Success/Reason contract is missing.
        var hookResultModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDMetadataNames.HookResultAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => HookResultModelFactory.Create(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);
        context.RegisterSourceOutput(
            hookResultModels
                .Where(static model => model.Diagnostic is not null)
                .Select(static (model, _) => model.Diagnostic!),
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        context.RegisterSourceOutput(
            hookResultModels.Select(static (model, _) => HookResultBuilderEmitter.Emit(model)),
            static (sourceContext, source) =>
            {
                if (source is not null)
                {
                    sourceContext.AddSource(
                        source.HintName,
                        Microsoft.CodeAnalysis.Text.SourceText.From(source.Source, System.Text.Encoding.UTF8));
                }
            });

        var hookFireAsyncModels = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DotBoxDMetadataNames.HookAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => HookFireAsyncModelFactory.Create(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect();
        context.RegisterSourceOutput(
            hookFireAsyncModels,
            static (sourceContext, models) => HookFireAsyncExtensionEmitter.Emit(sourceContext, models));
    }

    private static bool IsHookChainTerminal(SyntaxNode node)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "Run" or "RunLocal" or "Register" or "RegisterLocal"
            }
        };

    private static void RegisterDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<PluginKernelModelResult> results)
        => context.RegisterSourceOutput(
            results
                .Where(static result => result.Diagnostic is not null)
                .Select(static (result, _) => result.Diagnostic!)
                .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.DiagnosticResult),
            static (context, diagnostic) => context.ReportDiagnostic(diagnostic.ToDiagnostic()));

    private static void RegisterPackageSources(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<GeneratedPluginPackage> packages,
        IncrementalValueProvider<EquatableArray<GeneratedPluginPackageIdentity>> duplicateIdentities)
        => context.RegisterSourceOutput(
            packages
                .Combine(duplicateIdentities)
                .Where(static pair => !PluginPackageDuplicateDetector.Contains(pair.Right, pair.Left))
                .Select(static (pair, _) => pair.Left),
            static (context, package) => context.AddSource(package.HintName, package.Source));
}
