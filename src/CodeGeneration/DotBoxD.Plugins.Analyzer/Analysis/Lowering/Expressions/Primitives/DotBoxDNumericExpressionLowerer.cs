using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDNumericExpressionLowerer
{
    public static DotBoxDExpressionModel Unary(
        string helper,
        string symbol,
        DotBoxDExpressionModel operand)
    {
        if (!IsNumeric(operand))
        {
            throw new NotSupportedException($"Unary operator '{symbol}' requires numeric operands.");
        }

        return new DotBoxDExpressionModel($"{helper}({operand.Source})", operand.Type, operand.Allocates);
    }

    public static DotBoxDExpressionModel Binary(
        string helper,
        string symbol,
        DotBoxDExpressionModel left,
        DotBoxDExpressionModel right,
        bool comparison,
        bool allocates)
    {
        if (!IsNumeric(left) || !string.Equals(left.Type, right.Type, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Operator '{symbol}' requires matching numeric operands.");
        }

        return new DotBoxDExpressionModel(
            $"{helper}({left.Source}, {right.Source})",
            comparison ? DotBoxDGenerationNames.ManifestTypes.Bool : left.Type,
            allocates);
    }

    public static bool IsNumeric(DotBoxDExpressionModel expression)
        => DotBoxDGenerationNames.ManifestTypes.IsNumeric(expression.Type);
}
