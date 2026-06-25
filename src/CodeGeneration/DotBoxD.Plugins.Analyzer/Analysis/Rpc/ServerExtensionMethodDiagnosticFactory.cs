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

        var selected = SelectedBatchMethod(method.ContainingType, context.SemanticModel.Compilation);
        if (selected is null || !SymbolEqualityComparer.Default.Equals(selected, method))
        {
            return PluginKernelDiagnostic.Create(
                declaration.Identifier,
                "[ServerExtensionMethod] is ignored unless it is on the [ServerExtension] class's selected batch method.");
        }

        return null;
    }

    private static IMethodSymbol? SelectedBatchMethod(INamedTypeSymbol type, Compilation compilation)
    {
        IMethodSymbol? found = null;
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary,
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false
                } method ||
                method.Parameters.Length == 0 ||
                !RpcKernelContextParameter.IsSupported(method.Parameters[method.Parameters.Length - 1], compilation))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = method;
        }

        return found;
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
