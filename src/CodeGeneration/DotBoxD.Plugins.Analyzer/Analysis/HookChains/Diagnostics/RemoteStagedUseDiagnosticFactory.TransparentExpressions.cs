using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.HookChains;

internal static partial class RemoteStagedUseDiagnosticFactory
{
    private static ExpressionSyntax UnwrapTransparentParent(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression.Parent is ParenthesizedExpressionSyntax parenthesized &&
                parenthesized.Expression == expression)
            {
                expression = parenthesized;
                continue;
            }

            if (expression.Parent is PostfixUnaryExpressionSyntax postfix &&
                postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) &&
                postfix.Operand == expression)
            {
                expression = postfix;
                continue;
            }

            return expression;
        }
    }
}
