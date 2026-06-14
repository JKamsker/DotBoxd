namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrLiteralExpressionLowerer
{
    public static SafeIrExpressionModel Lower(LiteralExpressionSyntax literal)
        => literal.Token.Value switch {
            bool value => Bool(value),
            int value => Int32(value),
            long value => Int64(value),
            double value when IsFinite(value) => Float64(value),
            string value => String(value),
            _ => Unsupported(literal)
        };

    public static SafeIrExpressionModel? TryLowerNegative(PrefixUnaryExpressionSyntax unary)
    {
        if (unary.Kind() != SyntaxKind.UnaryMinusExpression ||
            unary.Operand is not LiteralExpressionSyntax literal)
        {
            return null;
        }

        return literal.Token.Value switch {
            int value when value != int.MinValue => Int32(-value),
            long value when value != long.MinValue => Int64(-value),
            double value when IsFinite(value) => Float64(-value),
            _ => null
        };
    }

    private static SafeIrExpressionModel Bool(bool value)
        => new(
            $"{SafeIrGenerationNames.Helpers.Bool}({BoolLiteral(value)})",
            SafeIrGenerationNames.ManifestTypes.Bool,
            false);

    private static SafeIrExpressionModel Int32(int value)
        => new(
            $"{SafeIrGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(value)})",
            SafeIrGenerationNames.ManifestTypes.Int,
            false);

    private static SafeIrExpressionModel Int64(long value)
        => new(
            $"{SafeIrGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(value)})",
            SafeIrGenerationNames.ManifestTypes.Long,
            false);

    private static SafeIrExpressionModel Float64(double value)
        => new(
            $"{SafeIrGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(value)})",
            SafeIrGenerationNames.ManifestTypes.Double,
            false);

    private static SafeIrExpressionModel String(string value)
        => new(
            $"{SafeIrGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            SafeIrGenerationNames.ManifestTypes.String,
            true);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static string BoolLiteral(bool value)
        => value
            ? SafeIrGenerationNames.CSharpLiterals.True
            : SafeIrGenerationNames.CSharpLiterals.False;

    private static SafeIrExpressionModel Unsupported(LiteralExpressionSyntax literal)
        => throw new NotSupportedException($"Unsupported plugin expression '{literal}'.");
}
