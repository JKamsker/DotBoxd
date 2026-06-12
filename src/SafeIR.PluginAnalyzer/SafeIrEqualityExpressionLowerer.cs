namespace SafeIR.PluginAnalyzer;

internal static class SafeIrEqualityExpressionLowerer
{
    public static SafeIrExpressionModel Lower(
        SafeIrExpressionModel left,
        SafeIrExpressionModel right,
        bool negate,
        bool allocates)
    {
        var symbol = negate
            ? SafeIrGenerationNames.Operators.NotEqualTo
            : SafeIrGenerationNames.Operators.EqualTo;
        if (!string.Equals(left.Type, right.Type, StringComparison.Ordinal)) {
            throw new NotSupportedException(
                $"Operator '{symbol}' requires operands with the same supported type.");
        }

        if (!SafeIrExpressionModelFactory.IsString(left)) {
            var helper = negate
                ? SafeIrGenerationNames.Helpers.Ne
                : SafeIrGenerationNames.Helpers.Eq;
            return Bool($"{helper}({left.Source}, {right.Source})", allocates);
        }

        var source = $"{SafeIrGenerationNames.Helpers.StringEquals}({left.Source}, {right.Source})";
        return Bool(negate ? $"{SafeIrGenerationNames.Helpers.Not}({source})" : source, allocates);
    }

    private static SafeIrExpressionModel Bool(string source, bool allocates)
        => new(source, SafeIrGenerationNames.ManifestTypes.Bool, allocates);
}
