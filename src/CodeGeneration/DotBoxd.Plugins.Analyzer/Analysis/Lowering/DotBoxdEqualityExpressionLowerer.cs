namespace DotBoxd.Plugins.Analyzer;

internal static class DotBoxdEqualityExpressionLowerer
{
    public static DotBoxdExpressionModel Lower(
        DotBoxdExpressionModel left,
        DotBoxdExpressionModel right,
        bool negate,
        bool allocates)
    {
        var symbol = negate
            ? DotBoxdGenerationNames.Operators.NotEqualTo
            : DotBoxdGenerationNames.Operators.EqualTo;
        if (!string.Equals(left.Type, right.Type, StringComparison.Ordinal)) {
            throw new NotSupportedException(
                $"Operator '{symbol}' requires operands with the same supported type.");
        }

        if (!DotBoxdExpressionModelFactory.IsString(left)) {
            var helper = negate
                ? DotBoxdGenerationNames.Helpers.Ne
                : DotBoxdGenerationNames.Helpers.Eq;
            return Bool($"{helper}({left.Source}, {right.Source})", allocates);
        }

        var source = $"{DotBoxdGenerationNames.Helpers.StringEquals}({left.Source}, {right.Source})";
        return Bool(negate ? $"{DotBoxdGenerationNames.Helpers.Not}({source})" : source, allocates);
    }

    private static DotBoxdExpressionModel Bool(string source, bool allocates)
        => new(source, DotBoxdGenerationNames.ManifestTypes.Bool, allocates);
}
