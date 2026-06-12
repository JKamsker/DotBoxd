namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrPatternExpressionLowerer
{
    public static SafeIrExpressionModel Lower(
        IsPatternExpressionSyntax expression,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var value = lowerExpression(expression.Expression);
        return LowerPattern(value, expression.Pattern, context, lowerExpression);
    }

    private static SafeIrExpressionModel LowerPattern(
        SafeIrExpressionModel value,
        PatternSyntax pattern,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
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

    private static SafeIrExpressionModel LowerConstant(
        SafeIrExpressionModel value,
        ConstantPatternSyntax pattern,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var constant = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        if (!string.Equals(value.Type, constant.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Pattern constant '{pattern.Expression}' must match the input expression type.");
        }

        return new SafeIrExpressionModel(
            $"{SafeIrGenerationNames.Helpers.Eq}({value.Source}, {constant.Source})",
            SafeIrGenerationNames.ManifestTypes.Bool,
            value.Allocates || constant.Allocates);
    }

    private static SafeIrExpressionModel LowerRelational(
        SafeIrExpressionModel value,
        RelationalPatternSyntax pattern,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        if (!SafeIrNumericExpressionLowerer.IsNumeric(value))
        {
            throw new NotSupportedException("Relational patterns require numeric input expressions.");
        }

        var comparand = LowerPatternValue(pattern.Expression, value.Type, context, lowerExpression);
        var (helper, symbol) = RelationalOperator(pattern);
        return SafeIrNumericExpressionLowerer.Binary(
            helper,
            symbol,
            value,
            comparand,
            comparison: true,
            value.Allocates || comparand.Allocates);
    }

    private static SafeIrExpressionModel LowerNot(
        SafeIrExpressionModel value,
        UnaryPatternSyntax pattern,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        var inner = LowerPattern(value, pattern.Pattern, context, lowerExpression);
        return new SafeIrExpressionModel(
            $"{SafeIrGenerationNames.Helpers.Not}({inner.Source})",
            SafeIrGenerationNames.ManifestTypes.Bool,
            inner.Allocates);
    }

    private static SafeIrExpressionModel LowerPatternValue(
        ExpressionSyntax expression,
        string targetType,
        SafeIrExpressionLoweringContext context,
        Func<ExpressionSyntax, SafeIrExpressionModel> lowerExpression)
    {
        if (SafeIrGenerationNames.ManifestTypes.IsNumeric(targetType) &&
            SafeIrNumericConstantPromoter.TryPromoteConstant(expression, context, targetType) is { } promoted)
        {
            return promoted;
        }

        return lowerExpression(expression);
    }

    private static (string Helper, string Symbol) RelationalOperator(RelationalPatternSyntax pattern)
        => pattern.OperatorToken.Kind() switch {
            SyntaxKind.GreaterThanEqualsToken => (
                SafeIrGenerationNames.Helpers.Ge,
                SafeIrGenerationNames.Operators.GreaterThanOrEqual),
            SyntaxKind.GreaterThanToken => (
                SafeIrGenerationNames.Helpers.Gt,
                SafeIrGenerationNames.Operators.GreaterThan),
            SyntaxKind.LessThanEqualsToken => (
                SafeIrGenerationNames.Helpers.Le,
                SafeIrGenerationNames.Operators.LessThanOrEqual),
            SyntaxKind.LessThanToken => (
                SafeIrGenerationNames.Helpers.Lt,
                SafeIrGenerationNames.Operators.LessThan),
            _ => throw new NotSupportedException(
                $"Unsupported relational pattern operator '{pattern.OperatorToken.ValueText}'.")
        };

    private static SafeIrExpressionModel Unsupported(PatternSyntax pattern)
        => throw new NotSupportedException($"Unsupported plugin pattern '{pattern}'.");
}
