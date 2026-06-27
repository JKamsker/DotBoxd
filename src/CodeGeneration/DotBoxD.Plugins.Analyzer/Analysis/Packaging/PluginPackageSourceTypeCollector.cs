using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginPackageSourceTypeCollector
{
    public static IncrementalValueProvider<System.Collections.Immutable.ImmutableArray<GeneratedPluginPackageIdentity>> Collect(
        IncrementalGeneratorInitializationContext context)
        => context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax,
                static (ctx, ct) => Identity(ctx, ct))
            .Where(static identity => identity.HasValue)
            .Select(static (identity, _) => identity!.Value)
            .Collect();

    private static GeneratedPluginPackageIdentity? Identity(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol
            {
                ContainingType: null
            } type)
        {
            return null;
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        return new GeneratedPluginPackageIdentity(ns, type.MetadataName);
    }
}
