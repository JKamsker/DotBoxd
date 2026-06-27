using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static class HookChainAliasResolver
{
    public static ExpressionSyntax UnwrapTransparentExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = postfix.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    public static ExpressionSyntax? Initializer(
        ExpressionSyntax expression,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = UnwrapTransparentExpression(expression);

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
                } declarator &&
                !IsAssignedAfterDeclaration(local, declarator, model, cancellationToken))
            {
                return initializer;
            }
        }

        return null;
    }

    private static bool IsAssignedAfterDeclaration(
        ILocalSymbol local,
        VariableDeclaratorSyntax declarator,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var root = declarator.SyntaxTree.GetRoot(cancellationToken);
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assignment.SpanStart <= declarator.SpanStart ||
                model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not ILocalSymbol assigned ||
                !SymbolEqualityComparer.Default.Equals(local, assigned))
            {
                continue;
            }

            return true;
        }

        return false;
    }
}
