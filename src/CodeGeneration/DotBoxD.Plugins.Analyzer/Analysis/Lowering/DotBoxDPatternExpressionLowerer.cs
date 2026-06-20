using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDPatternExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(
        IsPatternExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var value = lowerExpression(expression.Expression);
        return LowerPattern(value, expression.Pattern, context, lowerExpression);
    }

    private static DotBoxDExpressionModel LowerPattern(
        DotBoxDExpressionModel value,
        PatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
        => pattern switch
        {
            ParenthesizedPatternSyntax parenthesized =>
                LowerPattern(value, parenthesized.Pattern, context, lowerExpression),
            ConstantPatternSyntax constant =>
                LowerConstant(value, constant, context, lowerExpression),
            RelationalPatternSyntax relational =>
                LowerRelational(value, relational, context, lowerExpression),
            UnaryPatternSyntax unary when unary.Kind() == SyntaxKind.NotPattern =>
                LowerNot(value, unary, context, lowerExpression),
            _ => Unsupported(pattern)
        };

    private static DotBoxDExpressionModel LowerConstant(
        DotBoxDExpressionModel value,
        ConstantPatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var constant = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        if (!string.Equals(value.Type, constant.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Pattern constant '{pattern.Expression}' must match the input expression type.");
        }

        return DotBoxDEqualityExpressionLowerer.Lower(
            value,
            constant,
            negate: false,
            value.Allocates || constant.Allocates);
    }

    private static DotBoxDExpressionModel LowerRelational(
        DotBoxDExpressionModel value,
        RelationalPatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (!DotBoxDNumericExpressionLowerer.IsNumeric(value))
        {
            throw new NotSupportedException("Relational patterns require numeric input expressions.");
        }

        var comparand = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        var (helper, symbol) = RelationalOperator(pattern);
        return DotBoxDNumericExpressionLowerer.Binary(
            helper,
            symbol,
            value,
            comparand,
            comparison: true,
            value.Allocates || comparand.Allocates);
    }

    private static DotBoxDExpressionModel LowerNot(
        DotBoxDExpressionModel value,
        UnaryPatternSyntax pattern,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        var inner = LowerPattern(value, pattern.Pattern, context, lowerExpression);
        return new DotBoxDExpressionModel(
            $"{DotBoxDGenerationNames.Helpers.Not}({inner.Source})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            inner.Allocates);
    }

    private static DotBoxDExpressionModel LowerPatternValue(
        ExpressionSyntax expression,
        string targetType,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression)
    {
        if (DotBoxDGenerationNames.ManifestTypes.IsNumeric(targetType) &&
            DotBoxDNumericConstantPromoter.TryPromoteConstant(expression, context, targetType) is { } promoted)
        {
            return promoted;
        }

        return lowerExpression(expression);
    }

    private static (string Helper, string Symbol) RelationalOperator(RelationalPatternSyntax pattern)
        => pattern.OperatorToken.Kind() switch
        {
            SyntaxKind.GreaterThanEqualsToken => (
                DotBoxDGenerationNames.Helpers.Ge,
                DotBoxDGenerationNames.Operators.GreaterThanOrEqual),
            SyntaxKind.GreaterThanToken => (
                DotBoxDGenerationNames.Helpers.Gt,
                DotBoxDGenerationNames.Operators.GreaterThan),
            SyntaxKind.LessThanEqualsToken => (
                DotBoxDGenerationNames.Helpers.Le,
                DotBoxDGenerationNames.Operators.LessThanOrEqual),
            SyntaxKind.LessThanToken => (
                DotBoxDGenerationNames.Helpers.Lt,
                DotBoxDGenerationNames.Operators.LessThan),
            _ => throw new NotSupportedException(
                $"Unsupported relational pattern operator '{pattern.OperatorToken.ValueText}'.")
        };

    private static DotBoxDExpressionModel Unsupported(PatternSyntax pattern)
        => throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
}
