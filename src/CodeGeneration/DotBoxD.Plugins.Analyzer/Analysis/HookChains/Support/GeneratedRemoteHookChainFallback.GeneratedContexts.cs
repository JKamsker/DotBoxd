using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using DotBoxD.Plugins.Analyzer.Analysis.PluginServer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static string? InferredGeneratedContextTypeFullName(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        string? match = null;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(cancellationToken);
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (declaration.AttributeLists.Count == 0 ||
                    model.GetDeclaredSymbol(declaration, cancellationToken) is not INamedTypeSymbol type ||
                    !HasGeneratePluginServerAttribute(type))
                {
                    continue;
                }

                var contextName = PluginServerFacadeNameFormatter.ContextName(type.Name);
                var candidate = type.ContainingNamespace.IsGlobalNamespace
                    ? DotBoxDGenerationNames.TypeNames.GlobalPrefix + contextName
                    : DotBoxDGenerationNames.TypeNames.GlobalPrefix +
                      type.ContainingNamespace.ToDisplayString() + "." + contextName;
                if (match is not null && !string.Equals(match, candidate, StringComparison.Ordinal))
                {
                    return null;
                }

                match = candidate;
            }
        }

        return match;
    }

    private static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    DotBoxDGenerationNames.TypeNames.GeneratePluginServerAttribute,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
