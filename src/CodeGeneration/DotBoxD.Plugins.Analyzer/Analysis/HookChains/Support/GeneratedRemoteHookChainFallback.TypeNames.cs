using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class GeneratedRemoteHookChainFallback
{
    private static bool TypeMatches(
        TypeSyntax typeSyntax,
        string simpleName,
        string fullName,
        SemanticModel model,
        CancellationToken cancellationToken)
        => TypeNameMatches(typeSyntax.ToString(), simpleName, fullName) ||
           AliasTargetMatches(typeSyntax, simpleName, fullName, model, cancellationToken);

    private static bool AliasTargetMatches(
        TypeSyntax typeSyntax,
        string simpleName,
        string fullName,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (typeSyntax is not IdentifierNameSyntax identifier)
        {
            return false;
        }

        if (model.GetAliasInfo(identifier, cancellationToken)?.Target is not INamedTypeSymbol target)
        {
            return AliasSyntaxTargetMatches(identifier, simpleName, fullName, cancellationToken);
        }

        return TypeNameMatches(target.ToDisplayString(), simpleName, fullName) ||
            AliasSyntaxTargetMatches(identifier, simpleName, fullName, cancellationToken);
    }

    private static bool AliasSyntaxTargetMatches(
        IdentifierNameSyntax identifier,
        string simpleName,
        string fullName,
        CancellationToken cancellationToken)
    {
        foreach (var directive in InScopeUsingAliases(identifier, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directive.Alias?.Name.Identifier.ValueText == identifier.Identifier.ValueText &&
                directive.Name is { } targetName &&
                TypeNameMatches(targetName.ToString(), simpleName, fullName))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<UsingDirectiveSyntax> InScopeUsingAliases(
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        if (node.SyntaxTree.GetRoot(cancellationToken) is CompilationUnitSyntax compilationUnit)
        {
            foreach (var directive in compilationUnit.Usings)
            {
                yield return directive;
            }
        }

        foreach (var namespaceDeclaration in node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse())
        {
            foreach (var directive in namespaceDeclaration.Usings)
            {
                yield return directive;
            }
        }
    }

    private static bool TypeNameMatches(string text, string simpleName, string fullName)
    {
        const string GlobalPrefix = "global::";
        if (text.StartsWith(GlobalPrefix, StringComparison.Ordinal))
        {
            text = text.Substring(GlobalPrefix.Length);
        }

        return string.Equals(text, simpleName, StringComparison.Ordinal) ||
            string.Equals(text, fullName, StringComparison.Ordinal);
    }
}
