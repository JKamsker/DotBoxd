using System;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ShaRpcGenerator : IIncrementalGenerator
{
    private const string ShaRpcServiceAttributeName = "ShaRPC.Core.Attributes.ShaRpcServiceAttribute";

    private static readonly DiagnosticDescriptor s_generatorErrorRule = new(
        id: "SHARPC001",
        title: "ShaRPC source generator error",
        messageFormat: "ShaRPC failed to generate for '{0}': {1}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsupportedMethodRule = new(
        id: "SHARPC002",
        title: "Unsupported ShaRPC method shape",
        messageFormat: "ShaRPC cannot generate code for '{0}.{1}': {2}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_unsupportedServiceRule = new(
        id: "SHARPC003",
        title: "Unsupported ShaRPC service shape",
        messageFormat: "ShaRPC cannot generate code for service '{0}': {1}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor s_asyncSiblingCollisionRule = new(
        id: "SHARPC004",
        title: "ShaRPC async sibling method name collides",
        messageFormat: "ShaRPC cannot project '{0}.{1}' onto its async sibling: {2}",
        category: "ShaRPC.SourceGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ShaRpcServiceAttributeName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (ctx, ct) => ServiceModelFactory.GetServiceResult(ctx, ct))
            .WithTrackingName("RawServiceResults");

        var existingTypeDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    ExistingTypeIndex.IsPotentialGeneratedTypeDeclaration(node),
                transform: static (ctx, _) => ExistingTypeIndex.FromDeclaration(ctx.Node))
            .Where(static declaration => declaration is not null)
            .Select(static (declaration, _) => declaration!.Value)
            .WithTrackingName("ExistingTypeDeclarations");

        var existingTypeKeys = existingTypeDeclarations
            .Select(static (declaration, _) => declaration.Key)
            .WithTrackingName("ExistingTypeKeys");

        var existingTypes = existingTypeKeys
            .Collect()
            .Select(static (types, ct) => ExistingTypeIndex.Create(types, ct))
            .WithTrackingName("ExistingTypes");

        var existingTypeLocationIndex = existingTypeDeclarations
            .Collect()
            .Select(static (types, ct) => ExistingTypeLocationIndex.Create(types, ct))
            .WithTrackingName("ExistingTypeLocations");

        results = ServiceResultValidationPipeline.Apply(results, existingTypes);

        var errors = results
            .Where(static r => r.Error is not null)
            .Select(static (r, _) => r.Error!.Value)
            .WithTrackingName("ServiceErrors");

        context.RegisterSourceOutput(errors, static (spc, error) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_generatorErrorRule,
                Location.None,
                error.Where,
                error.Message)));

        var methodDiagnostics = results
            .SelectMany(static (r, _) => r.MethodDiagnostics.Array)
            .WithTrackingName("MethodDiagnostics");

        context.RegisterSourceOutput(methodDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedMethodRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.MethodName,
                d.Reason)));

        var serviceDiagnostics = results
            .Where(static r => r.ServiceDiagnostic is not null)
            .Select(static (r, _) => r.ServiceDiagnostic!.Value)
            .WithTrackingName("ServiceDiagnostics");

        context.RegisterSourceOutput(serviceDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedServiceRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.Reason)));

        var existingTypeDiagnostics = results
            .Where(static r => r.ExistingTypeCollision is not null)
            .Select(static (r, _) => r.ExistingTypeCollision!.Value)
            .Combine(existingTypeLocationIndex)
            .Select(static (pair, ct) => new ServiceDiagnostic(
                pair.Left.InterfaceName,
                pair.Left.Reason,
                pair.Right.Find(pair.Left.ExistingType, ct)))
            .WithTrackingName("ExistingTypeDiagnostics");

        context.RegisterSourceOutput(existingTypeDiagnostics, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_unsupportedServiceRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.Reason)));

        var models = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .WithTrackingName("Services");

        var projections = results
            .Where(static r => r.Model is not null)
            .Select(static (r, ct) =>
            {
                var model = r.Model!;
                if (!NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
                {
                    return new ServiceProjection(
                        ServiceBundle.Empty(model),
                        EquatableArray<MethodDiagnostic>.Empty);
                }

                var (siblings, collisions) = AsyncSiblingProjector.Compute(model, r.MethodLocations, ct);
                return new ServiceProjection(new ServiceBundle(model, siblings), collisions);
            })
            .WithTrackingName("ServiceProjections");

        var bundles = projections
            .Select(static (projection, _) => projection.Bundle)
            .WithTrackingName("ServiceBundles");

        var siblingCollisions = projections
            .SelectMany(static (projection, _) => projection.SiblingCollisions.Array)
            .WithTrackingName("SiblingCollisions");

        context.RegisterSourceOutput(siblingCollisions, static (spc, d) =>
            spc.ReportDiagnostic(Diagnostic.Create(
                s_asyncSiblingCollisionRule,
                d.Location.ToLocation(),
                d.InterfaceName,
                d.MethodName,
                d.Reason)));

        context.RegisterSourceOutput(bundles, static (spc, bundle) =>
        {
            try
            {
                var hintPrefix = HintNameBuilder.Prefix(bundle.Model);
                var ct = spc.CancellationToken;
                var proxySource = ProxyGenerator.Generate(bundle.Model, bundle.SiblingMethods, ct);
                spc.AddSource(
                    $"{hintPrefix}.ShaRpcProxy.g.cs",
                    SourceText.From(proxySource, Encoding.UTF8));

                var dispatcherSource = DispatcherGenerator.Generate(bundle.Model, ct);
                spc.AddSource(
                    $"{hintPrefix}.ShaRpcDispatcher.g.cs",
                    SourceText.From(dispatcherSource, Encoding.UTF8));

                if (!bundle.SiblingMethods.IsEmpty)
                {
                    var asyncSource = AsyncInterfaceGenerator.Generate(bundle.Model, bundle.SiblingMethods, ct);
                    spc.AddSource(
                        $"{hintPrefix}.ShaRpcAsync.g.cs",
                        SourceText.From(asyncSource, Encoding.UTF8));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    bundle.Model.InterfaceName,
                    ex.ToString()));
            }
        });

        var allServices = models
            .Select(static (model, _) => ServiceIdentity.From(model))
            .Collect()
            .Select(static (arr, ct) => ServiceModelOrdering.SortIdentities(arr, ct))
            .WithTrackingName("AllServices");

        var allServiceMetadata = models
            .Collect()
            .Select(static (arr, ct) => ServiceModelOrdering.Sort(arr, ct))
            .WithTrackingName("AllServiceMetadata");

        context.RegisterSourceOutput(allServices, static (spc, services) =>
        {
            if (services.IsEmpty)
            {
                return;
            }

            try
            {
                var extensionsSource = ExtensionsGenerator.Generate(services, spc.CancellationToken);
                spc.AddSource(
                    "ShaRpcExtensions.g.cs",
                    SourceText.From(extensionsSource, Encoding.UTF8));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    "ShaRpcExtensions",
                    ex.ToString()));
            }
        });

        context.RegisterSourceOutput(allServiceMetadata, static (spc, services) =>
        {
            if (services.IsEmpty)
            {
                return;
            }

            try
            {
                var factorySource = GeneratedFactoryGenerator.Generate(services, spc.CancellationToken);
                spc.AddSource(
                    "ShaRpcGenerated.g.cs",
                    SourceText.From(factorySource, Encoding.UTF8));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    s_generatorErrorRule,
                    Location.None,
                    "ShaRpcGenerated",
                    ex.ToString()));
            }
        });
    }
}
