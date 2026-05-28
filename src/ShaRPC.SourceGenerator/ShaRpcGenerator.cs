using System;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ShaRPC.SourceGenerator;

/// <summary>
/// Incremental source generator for ShaRPC client proxies and server dispatchers.
/// </summary>
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

        var existingTypes = context.CompilationProvider
            .Select(static (compilation, ct) => ExistingTypeIndex.Create(compilation, ct))
            .WithTrackingName("ExistingTypes");

        results = results
            .Combine(existingTypes)
            .Select(static (pair, ct) =>
                GeneratedTypeCollisionValidator.Apply(pair.Left, pair.Right, ct))
            .WithTrackingName("ExistingTypeValidatedServiceResults");

        var generatedServiceNames = results
            .Collect()
            .Select(static (arr, ct) => GeneratedServiceNameIndex.Create(arr, ct))
            .WithTrackingName("GeneratedServiceNames");

        results = results
            .Combine(generatedServiceNames)
            .Select(static (pair, ct) =>
                GeneratedServiceCollisionValidator.Apply(pair.Left, pair.Right, ct))
            .WithTrackingName("GeneratedServiceValidatedServiceResults");

        var rejectedServices = results
            .Collect()
            .Select(static (arr, ct) => RejectedServiceIndex.Create(arr, ct))
            .WithTrackingName("RejectedServices");

        results = results
            .Combine(rejectedServices)
            .Select(static (pair, ct) =>
                SubServiceAvailabilityValidator.Apply(pair.Left, pair.Right, ct))
            .WithTrackingName("ServiceResults");

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

        // SHARPC002 — per-method diagnostics for shapes ShaRPC cannot generate (ref/in/out).
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

        // SHARPC003 — service-level diagnostics (generic / nested service interfaces).
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

        var models = results
            .Where(static r => r.Model is not null)
            .Select(static (r, _) => r.Model!)
            .WithTrackingName("Services");

        // Bundle each model with its async-sibling projection so every generated source
        // output flows through one value-equatable record.
        var bundles = models
            .Select(static (m, ct) =>
            {
                if (!NamingHelpers.CanGenerateAsyncSiblingInterface(m.InterfaceName))
                {
                    return ServiceBundle.Empty(m);
                }

                var (siblings, _) = AsyncSiblingProjector.Compute(m, ct);
                return new ServiceBundle(m, siblings);
            })
            .WithTrackingName("ServiceBundles");

        // SHARPC004 — async-sibling naming collision warnings.
        var siblingCollisions = results
            .Where(static r => r.Model is not null)
            .SelectMany(static (r, ct) =>
            {
                var model = r.Model!;
                if (!NamingHelpers.CanGenerateAsyncSiblingInterface(model.InterfaceName))
                {
                    return EquatableArray<MethodDiagnostic>.Empty.Array;
                }

                var (_, collisions) = AsyncSiblingProjector.Compute(model, r.MethodLocations, ct);
                return collisions.Array;
            })
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
            .Collect()
            .Select(static (arr, _) => arr
                .OrderBy(static service => service.Namespace, StringComparer.Ordinal)
                .ThenBy(static service => service.InterfaceName, StringComparer.Ordinal)
                .ThenBy(static service => service.ServiceName, StringComparer.Ordinal)
                .ToEquatableArray())
            .WithTrackingName("AllServices");

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
    }

}
