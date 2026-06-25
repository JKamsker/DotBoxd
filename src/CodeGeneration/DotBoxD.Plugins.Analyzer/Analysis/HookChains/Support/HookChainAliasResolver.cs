using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainAliasResolver
{
    public static ExpressionSyntax? Initializer(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        if (expression is not IdentifierNameSyntax identifier ||
            model.GetSymbolInfo(identifier, cancellationToken).Symbol is not ILocalSymbol local)
        {
            return null;
        }

        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
                {
                    Initializer.Value: { } initializer
                })
            {
                return initializer;
            }
        }

        return null;
    }
}
