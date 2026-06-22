using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDEqualityExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool negate,
        bool allocates)
    {
        var symbol = negate
            ? DotBoxDGenerationNames.Operators.NotEqualTo
            : DotBoxDGenerationNames.Operators.EqualTo;
        if (!string.Equals(left.Type, right.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Operator '{symbol}' requires operands with the same supported type.");
        }

        // Only scalar operands may be compared. The sandbox compares a list/map/record value by STRUCTURE, but C#
        // `==` on an array, List<T>, dictionary, or ordinary class is REFERENCE equality — so lowering
        // `e.Ids == e.OtherIds` would silently change the predicate's meaning. Reject non-scalars (fail safe).
        if (!IsEquatableScalar(left.Type))
        {
            throw new NotSupportedException(
                $"Operator '{symbol}' is only supported on scalar operands; '{left.Type}' would compare by structure " +
                "in the sandbox but by reference in C#.");
        }

        if (!DotBoxDExpressionModelFactory.IsString(left))
        {
            var helper = negate
                ? DotBoxDGenerationNames.Helpers.Ne
                : DotBoxDGenerationNames.Helpers.Eq;
            return Bool($"{helper}({left.Source}, {right.Source})", allocates);
        }

        var source = $"{DotBoxDGenerationNames.Helpers.StringEquals}({left.Source}, {right.Source})";
        return Bool(negate ? $"{DotBoxDGenerationNames.Helpers.Not}({source})" : source, allocates);
    }

    private static DotBoxDExpressionModel Bool(string source, bool allocates)
        => new(source, DotBoxDGenerationNames.ManifestTypes.Bool, allocates);

    // The scalar tags that carry C#-equivalent equality: bool/int/long/double/string (and Guid, a value type with
    // structural equality matching the sandbox). Enums ride as int/long, so they are covered. List/map/record tags
    // are intentionally excluded.
    private static bool IsEquatableScalar(string tag)
        => string.Equals(tag, DotBoxDGenerationNames.ManifestTypes.Bool, StringComparison.Ordinal)
        || string.Equals(tag, DotBoxDGenerationNames.ManifestTypes.Int, StringComparison.Ordinal)
        || string.Equals(tag, DotBoxDGenerationNames.ManifestTypes.Long, StringComparison.Ordinal)
        || string.Equals(tag, DotBoxDGenerationNames.ManifestTypes.Double, StringComparison.Ordinal)
        || string.Equals(tag, DotBoxDGenerationNames.ManifestTypes.String, StringComparison.Ordinal)
        || string.Equals(tag, DotBoxDGenerationNames.ManifestTypes.Guid, StringComparison.Ordinal);
}
