namespace DotBoxd.Plugins.Analyzer;

internal static class DotBoxdNumericExpressionLowerer
{
    public static DotBoxdExpressionModel Unary(
        string helper,
        string symbol,
        DotBoxdExpressionModel operand)
    {
        if (!IsNumeric(operand))
        {
            throw new NotSupportedException($"Unary operator '{symbol}' requires numeric operands.");
        }

        return new DotBoxdExpressionModel($"{helper}({operand.Source})", operand.Type, operand.Allocates);
    }

    public static DotBoxdExpressionModel Binary(
        string helper,
        string symbol,
        DotBoxdExpressionModel left,
        DotBoxdExpressionModel right,
        bool comparison,
        bool allocates)
    {
        if (!IsNumeric(left) || !string.Equals(left.Type, right.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Operator '{symbol}' requires matching numeric operands.");
        }

        return new DotBoxdExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            comparison ? DotBoxdGenerationNames.ManifestTypes.Bool : left.Type,
            allocates);
    }

    public static bool IsNumeric(DotBoxdExpressionModel expression)
        => DotBoxdGenerationNames.ManifestTypes.IsNumeric(expression.Type);
}
