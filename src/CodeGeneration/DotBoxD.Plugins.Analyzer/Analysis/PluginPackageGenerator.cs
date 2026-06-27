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
        var pluginAttributeResults = GeneratorGuard.AttributeValues(
            context,
            DotBoxDMetadataNames.PluginAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            "plugin kernel model",
            static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct));
        var eventKernelAttributeResults = GeneratorGuard.AttributeValues(
            context,
            DotBoxDMetadataNames.EventKernelAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            "event kernel model",
            static (ctx, ct) => PluginKernelModelFactory.Create(ctx, ct));
        PluginPackageGeneratorOutput.RegisterDiagnostics(context, pluginAttributeResults);
        PluginPackageGeneratorOutput.RegisterDiagnostics(context, eventKernelAttributeResults);

        var pluginModels = pluginAttributeResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.ModelResult);
        var eventKernelModels = eventKernelAttributeResults
            .Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.ModelResult);

        var pluginPackages = GeneratorGuard.TransformValues(
                context,
                pluginModels,
                "plugin package source",
                static (model, _) => DotBoxDPackageSourceEmitter.Emit(model))
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);
        var eventKernelPackages = GeneratorGuard.TransformValues(
                context,
                eventKernelModels,
                "event kernel package source",
                static (model, _) => DotBoxDPackageSourceEmitter.Emit(model))
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
        var chainCreateResults = GeneratorGuard.SyntaxValues(
            context,
            static (node, _) => IsHookChainTerminal(node),
            "hook chain model",
            static (syntaxContext, ct) => HookChainModelFactory.Create(syntaxContext, ct));
        var remoteStagedUseDiagnostics = GeneratorGuard.SyntaxValues(
            context,
            static (node, _) => RemoteStagedUseDiagnosticFactory.IsCandidate(node),
            "remote staged-use diagnostic",
            static (syntaxContext, ct) => RemoteStagedUseDiagnosticFactory.Create(syntaxContext, ct));
        GeneratorGuard.RegisterOutput(
            context,
            remoteStagedUseDiagnostics,
            "remote staged-use diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        // Report DBXK111 for a recognized remote RunLocal chain whose stages could not be lowered, so the
        // otherwise-silent skip (and the runtime NotSupportedException it leads to) is visible at build time.
        GeneratorGuard.RegisterOutput(
            context,
            chainCreateResults
                .Where(static result => result.Diagnostic is not null)
                .Select(static (result, _) => result.Diagnostic!),
            "hook chain not-lowered diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        GeneratorGuard.RegisterOutput(
            context,
            chainCreateResults
                .Where(static result => result.UnsupportedDiagnostic is not null)
                .Select(static (result, _) => result.UnsupportedDiagnostic!),
            "hook chain unsupported diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var chainResults = chainCreateResults
            .Where(static result => result.Chain is not null)
            .Select(static (result, _) => result.Chain!);

        // Server extensions: lower a [ServerExtension] class's batch method to a verified-IR package
        // whose Create() imports the JSON. Unsupported shapes emit a diagnostic and no package.
        var rpcResults = GeneratorGuard.AttributeValues(
            context,
            DotBoxDMetadataNames.ServerExtensionAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            "server extension package model",
            static (ctx, ct) => RpcKernelModelFactory.Create(ctx, ct));

        GeneratorGuard.RegisterOutput(
            context,
            rpcResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            "server extension diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var serverExtensionMethodDiagnostics = GeneratorGuard.AttributeValues(
            context,
            DotBoxDMetadataNames.ServerExtensionMethodAttribute,
            static (node, _) => node is MethodDeclarationSyntax,
            "server extension method diagnostic",
            static (ctx, ct) => ServerExtensionMethodDiagnosticFactory.Create(ctx, ct));
        GeneratorGuard.RegisterOutput(
            context,
            serverExtensionMethodDiagnostics,
            "server extension method diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));

        var rpcPackages = rpcResults
            .Where(static result => result.Package is not null)
            .Select(static (result, _) => result.Package!)
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);

        var invokeAsyncResults = GeneratorGuard.SyntaxValues(
            context,
            static (node, _) => node is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "InvokeAsync" }
                    or IdentifierNameSyntax { Identifier.ValueText: "InvokeAsync" }
                    or GenericNameSyntax { Identifier.ValueText: "InvokeAsync" }
            },
            "InvokeAsync package model",
            static (syntaxContext, ct) => InvokeAsyncModelFactory.Create(syntaxContext, ct));
        GeneratorGuard.RegisterOutput(
            context,
            invokeAsyncResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            "InvokeAsync diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        GeneratorGuard.RegisterOutput(
            context,
            invokeAsyncResults.Where(static result => result.Package is not null).Select(static (result, _) => result.Package!),
            "InvokeAsync source output",
            static (sourceContext, package) => sourceContext.AddSource(package.HintName, package.Source));

        var pluginServerResults = GeneratorGuard.AttributeValues(
            context,
            DotBoxDMetadataNames.GeneratePluginServerAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            "plugin server facade model",
            static (ctx, ct) => PluginServerFacadeModelFactory.Create(ctx, ct));
        GeneratorGuard.RegisterOutput(
            context,
            pluginServerResults.Where(static result => result.Diagnostic is not null).Select(static (result, _) => result.Diagnostic!),
            "plugin server facade diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        GeneratorGuard.RegisterOutput(
            context,
            pluginServerResults.Where(static result => result.Source is not null).Select(static (result, _) => result.Source!),
            "plugin server facade source output",
            static (sourceContext, source) => sourceContext.AddSource(source.HintName, source.Source));

        var chainPackages = GeneratorGuard.TransformValues(
                context,
                chainResults,
                "hook chain package source",
                static (result, _) => DotBoxDPackageSourceEmitter.Emit(result.Model))
            .WithTrackingName(DotBoxDPluginPackageGeneratorTrackingNames.PackageResult);
        var chainPackageIdentities = chainPackages
            .Select(static (package, _) => GeneratedPluginPackageIdentity.From(package))
            .Collect();
        var rpcPackageIdentities = rpcPackages
            .Select(static (package, _) => GeneratedPluginPackageIdentity.From(package))
            .Collect();
        var rpcGraftCollisions = GeneratorGuard.TransformValueOrDefault(
            context,
            rpcResults.Collect(),
            "server extension graft collision detection",
            static (results, _) => RpcKernelGraftCollisionDetector.FindDuplicates(results));
        GeneratorGuard.RegisterOutput(
            context,
            rpcGraftCollisions.SelectMany(static (collisions, _) => RpcKernelGraftCollisionDetector.Diagnostics(collisions)),
            "server extension graft collision diagnostic output",
            static (context, diagnostic) => context.ReportDiagnostic(diagnostic));
        var blockedIdentities = PluginPackageCollisionProviders.RegisterBlockedIdentities(
            context,
            pluginPackageIdentities,
            eventKernelPackageIdentities,
            chainPackageIdentities,
            rpcPackageIdentities);
        PluginPackageGeneratorOutput.RegisterPackageSources(context, pluginPackages, blockedIdentities);
        PluginPackageGeneratorOutput.RegisterPackageSources(context, eventKernelPackages, blockedIdentities);
        PluginPackageGeneratorOutput.RegisterPackageSources(context, chainPackages, blockedIdentities);
        PluginPackageGeneratorOutput.RegisterPackageSources(context, rpcPackages, blockedIdentities);

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
        GeneratorGuard.RegisterOutput(
            context,
            interceptions,
            "hook chain interceptor source output",
            static (sourceContext, items) => DotBoxDHookChainInterceptorEmitter.Emit(sourceContext, items));
        GeneratorGuard.RegisterOutput(
            context,
            invokeAsyncInterceptions,
            "InvokeAsync interceptor source output",
            static (sourceContext, items) => InvokeAsyncInterceptorEmitter.Emit(sourceContext, items));

        RegistrationAccumulatorGenerator.Register(context);

        // [HookResult] builder surface: Ok()/Reject()/With<Field>() for each annotated result record, plus the
        // DBXK112 diagnostic when the Success/Reason contract is missing.
        var hookResultModels = GeneratorGuard.AttributeValues(
            context,
            DotBoxDMetadataNames.HookResultAttribute,
            static (node, _) => node is TypeDeclarationSyntax,
            "hook result model",
            static (ctx, ct) => HookResultModelFactory.Create(ctx, ct));
        GeneratorGuard.RegisterOutput(
            context,
            hookResultModels
                .Where(static model => model.Diagnostic is not null)
                .Select(static (model, _) => model.Diagnostic!),
            "hook result diagnostic output",
            static (sourceContext, diagnostic) => sourceContext.ReportDiagnostic(diagnostic.ToDiagnostic()));
        var hookResultBuilderSources = GeneratorGuard.TransformNullableValues(
            context,
            hookResultModels,
            "hook result builder source",
            static (model, _) => HookResultBuilderEmitter.Emit(model));
        GeneratorGuard.RegisterOutput(
            context,
            hookResultBuilderSources,
            "hook result builder source output",
            static (sourceContext, source) => sourceContext.AddSource(
                source.HintName,
                Microsoft.CodeAnalysis.Text.SourceText.From(source.Source, System.Text.Encoding.UTF8)));

        var hookFireAsyncModels = GeneratorGuard.AttributeValues(
                context,
                DotBoxDMetadataNames.HookAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                "hook FireAsync extension model",
                static (ctx, ct) => HookFireAsyncModelFactory.Create(ctx, ct))
            .Collect();
        GeneratorGuard.RegisterOutput(
            context,
            hookFireAsyncModels,
            "hook FireAsync extension source output",
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

}
