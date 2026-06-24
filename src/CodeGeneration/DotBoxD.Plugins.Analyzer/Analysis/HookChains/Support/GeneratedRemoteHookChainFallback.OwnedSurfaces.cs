using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.PluginServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static IEnumerable<OwnedGeneratedSurface> OwnedGeneratedSurfaces(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot(cancellationToken).DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (declaration.AttributeLists.Count == 0 ||
                    model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol serverType ||
                    !HasGeneratePluginServerAttribute(serverType, compilation) ||
                    ResolveWorldType(serverType) is not { } worldType)
                {
                    continue;
                }

                if (OwnedGeneratedSurface.Create(serverType, worldType, compilation) is { } surface)
                {
                    yield return surface;
                }
            }
        }
    }

    private static INamedTypeSymbol? ResolveWorldType(INamedTypeSymbol serverType)
    {
        foreach (var candidate in serverType.Interfaces)
        {
            if (HasAttribute(candidate, DotBoxDMetadataNames.DotBoxDServiceAttribute))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record OwnedGeneratedSurface(
        string ServerInterfaceName,
        string ServerInterfaceFullName,
        string HookRegistryName,
        string HookRegistryFullName,
        string SubscriptionRegistryName,
        string SubscriptionRegistryFullName,
        string ContextFullName)
    {
        public static OwnedGeneratedSurface? Create(
            INamedTypeSymbol serverType,
            INamedTypeSymbol worldType,
            Compilation compilation)
        {
            var contextFullName = GeneratedContextTypeFullName(serverType, compilation);
            if (contextFullName is null)
            {
                return null;
            }

            var ns = serverType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : serverType.ContainingNamespace.ToDisplayString();
            var prefix = string.IsNullOrEmpty(ns) ? string.Empty : ns + ".";
            var serverInterfaceName = PluginServerFacadeNameFormatter.ServerInterfaceName(worldType);
            var hookRegistryName = PluginServerFacadeNameFormatter.HookRegistryName(serverType.Name);
            var subscriptionRegistryName = PluginServerFacadeNameFormatter.SubscriptionRegistryName(serverType.Name);
            return new OwnedGeneratedSurface(
                serverInterfaceName,
                prefix + serverInterfaceName,
                hookRegistryName,
                prefix + hookRegistryName,
                subscriptionRegistryName,
                prefix + subscriptionRegistryName,
                contextFullName);
        }
    }
}
