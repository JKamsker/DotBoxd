using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class ServerExtensionMethodDiagnosticFactory
{
    public static PluginKernelDiagnostic? Create(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TargetSymbol is not IMethodSymbol method ||
            context.TargetNode is not MethodDeclarationSyntax declaration)
        {
            return null;
        }

        if (!HasAttribute(method.ContainingType, context.SemanticModel.Compilation, DotBoxDMetadataNames.ServerExtensionAttribute))
        {
            return PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "[ServerExtensionMethod] must be placed on the selected batch method of a [ServerExtension] class.");
        }

        var selected = RpcKernelModelFactory.TryResolveBatchMethod(method.ContainingType, context.SemanticModel.Compilation);
        if (selected is null || !SymbolEqualityComparer.Default.Equals(selected, method))
        {
            return PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "[ServerExtensionMethod] is ignored unless it is on the [ServerExtension] class's selected batch method.");
        }

        return null;
    }

    private static bool HasAttribute(INamedTypeSymbol type, Compilation compilation, string metadataName)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (compilation.GetTypeByMetadataName(metadataName) is { } expected &&
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, expected))
            {
                return true;
            }
        }

        return false;
    }
}
