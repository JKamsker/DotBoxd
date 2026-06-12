namespace SafeIR.PluginAnalyzer;

internal static class SafeIrNumericExpressionLowerer
{
    public static SafeIrExpressionModel Unary(
        string helper,
        string symbol,
        SafeIrExpressionModel operand)
    {
        if (!IsNumeric(operand))
        {
            throw new NotSupportedException($"Unary operator '{symbol}' requires numeric operands.");
        }

        return new SafeIrExpressionModel($"{helper}({operand.Source})", operand.Type, operand.Allocates);
    }

    public static SafeIrExpressionModel Binary(
        string helper,
        string symbol,
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        bool comparison,
        bool allocates)
    {
        if (!IsNumeric(left) || !string.Equals(left.Type, right.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Operator '{symbol}' requires matching numeric operands.");
        }

        return new SafeIrExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            comparison ? SafeIrGenerationNames.ManifestTypes.Bool : left.Type,
            allocates);
    }

    public static bool IsNumeric(SafeIrExpressionModel expression)
        => SafeIrGenerationNames.ManifestTypes.IsNumeric(expression.Type);
}
