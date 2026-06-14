namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdPatternExpressionLowerer
{
    public static DotBoxdExpressionModel Lower(
        IsPatternExpressionSyntax expression,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        var value = lowerExpression(expression.Expression);
        return LowerPattern(value, expression.Pattern, context, lowerExpression);
    }

    private static DotBoxdExpressionModel LowerPattern(
        DotBoxdExpressionModel value,
        PatternSyntax pattern,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
        => pattern switch {
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

    private static DotBoxdExpressionModel LowerConstant(
        DotBoxdExpressionModel value,
        ConstantPatternSyntax pattern,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        var constant = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        if (!string.Equals(value.Type, constant.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Pattern constant '{pattern.Expression}' must match the input expression type.");
        }

        return DotBoxdEqualityExpressionLowerer.Lower(
            value,
            constant,
            negate: false,
            value.Allocates || constant.Allocates);
    }

    private static DotBoxdExpressionModel LowerRelational(
        DotBoxdExpressionModel value,
        RelationalPatternSyntax pattern,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        if (!DotBoxdNumericExpressionLowerer.IsNumeric(value))
        {
            throw new NotSupportedException("Relational patterns require numeric input expressions.");
        }

        var comparand = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        var (helper, symbol) = RelationalOperator(pattern);
        return DotBoxdNumericExpressionLowerer.Binary(
            helper,
            symbol,
            value,
            comparand,
            comparison: true,
            value.Allocates || comparand.Allocates);
    }

    private static DotBoxdExpressionModel LowerNot(
        DotBoxdExpressionModel value,
        UnaryPatternSyntax pattern,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        var inner = LowerPattern(value, pattern.Pattern, context, lowerExpression);
        return new DotBoxdExpressionModel(
            $"{DotBoxdGenerationNames.Helpers.Not}({inner.Source})",
            DotBoxdGenerationNames.ManifestTypes.Bool,
            inner.Allocates);
    }

    private static DotBoxdExpressionModel LowerPatternValue(
        ExpressionSyntax expression,
        string targetType,
        DotBoxdExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxdExpressionModel> lowerExpression)
    {
        if (DotBoxdGenerationNames.ManifestTypes.IsNumeric(targetType) &&
            DotBoxdNumericConstantPromoter.TryPromoteConstant(expression, context, targetType) is { } promoted)
        {
            return promoted;
        }

        return lowerExpression(expression);
    }

    private static (string Helper, string Symbol) RelationalOperator(RelationalPatternSyntax pattern)
        => pattern.OperatorToken.Kind() switch {
            SyntaxKind.GreaterThanEqualsToken => (
                DotBoxdGenerationNames.Helpers.Ge,
                DotBoxdGenerationNames.Operators.GreaterThanOrEqual),
            SyntaxKind.GreaterThanToken => (
                DotBoxdGenerationNames.Helpers.Gt,
                DotBoxdGenerationNames.Operators.GreaterThan),
            SyntaxKind.LessThanEqualsToken => (
                DotBoxdGenerationNames.Helpers.Le,
                DotBoxdGenerationNames.Operators.LessThanOrEqual),
            SyntaxKind.LessThanToken => (
                DotBoxdGenerationNames.Helpers.Lt,
                DotBoxdGenerationNames.Operators.LessThan),
            _ => throw new NotSupportedException(
                $"Unsupported relational pattern operator '{pattern.OperatorToken.ValueText}'.")
        };

    private static DotBoxdExpressionModel Unsupported(PatternSyntax pattern)
        => throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
}
