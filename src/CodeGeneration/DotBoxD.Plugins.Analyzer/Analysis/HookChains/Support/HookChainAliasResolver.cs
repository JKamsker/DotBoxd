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
                !IsMutatedBetweenDeclarationAndUse(local, declarator, identifier, model, cancellationToken))
            {
                return initializer;
            }
        }

        return null;
    }

    public static bool HasMutationBetween(
        ILocalSymbol local,
        int start,
        int end,
        SemanticModel model,
        CancellationToken cancellationToken,
        SyntaxNode? root = null,
        bool descendIntoNestedFunctions = false,
        SyntaxNode? nestedFunctionPathNode = null)
    {
        root ??= LocalDeclarationBlock(local, cancellationToken);
        if (root is null)
        {
            return false;
        }

        var nestedFunctionPath = nestedFunctionPathNode is null
            ? null
            : new HashSet<SyntaxNode>(nestedFunctionPathNode.AncestorsAndSelf());
        foreach (var node in root.DescendantNodes(node =>
            descendIntoNestedFunctions ||
            node is not LambdaExpressionSyntax and not LocalFunctionStatementSyntax ||
            (nestedFunctionPath is not null && nestedFunctionPath.Contains(node))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.SpanStart <= start || node.SpanStart >= end)
            {
                continue;
            }

            if (IsMutationOfLocal(node, local, model, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMutatedBetweenDeclarationAndUse(
        ILocalSymbol local,
        VariableDeclaratorSyntax declarator,
        IdentifierNameSyntax use,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var root = declarator.SyntaxTree.GetRoot(cancellationToken);
        return HasMutationBetween(
            local,
            declarator.SpanStart,
            use.SpanStart,
            model,
            cancellationToken,
            root,
            nestedFunctionPathNode: use);
    }

    private static SyntaxNode? LocalDeclarationBlock(ILocalSymbol local, CancellationToken cancellationToken)
        => local.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken).FirstAncestorOrSelf<BlockSyntax>())
            .FirstOrDefault(candidate => candidate is not null);

    private static bool IsMutationOfLocal(
        SyntaxNode node,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        switch (node)
        {
            case AssignmentExpressionSyntax assignment:
                return ExpressionNamesLocal(assignment.Left, local, model, cancellationToken);
            case ArgumentSyntax argument when IsWritableByRef(argument):
                return ExpressionNamesLocal(argument.Expression, local, model, cancellationToken);
            case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                prefix.IsKind(SyntaxKind.PreDecrementExpression):
                return ExpressionNamesLocal(prefix.Operand, local, model, cancellationToken);
            case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) ||
                postfix.IsKind(SyntaxKind.PostDecrementExpression):
                return ExpressionNamesLocal(postfix.Operand, local, model, cancellationToken);
            default:
                return false;
        }
    }

    private static bool IsWritableByRef(ArgumentSyntax argument)
        => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
           argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);

    private static bool ExpressionNamesLocal(
        ExpressionSyntax expression,
        ILocalSymbol local,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        expression = UnwrapTransparentExpression(expression);
        if (expression is IdentifierNameSyntax identifier &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, local))
        {
            return true;
        }

        if (expression is TupleExpressionSyntax tuple)
        {
            foreach (var argument in tuple.Arguments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ExpressionNamesLocal(argument.Expression, local, model, cancellationToken))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
