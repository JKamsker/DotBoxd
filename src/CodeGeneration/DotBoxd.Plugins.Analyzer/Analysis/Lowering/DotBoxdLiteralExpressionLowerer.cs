namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdLiteralExpressionLowerer
{
    public static DotBoxdExpressionModel Lower(LiteralExpressionSyntax literal)
        => literal.Token.Value switch {
            bool value => Bool(value),
            int value => Int32(value),
            long value => Int64(value),
            double value when IsFinite(value) => Float64(value),
            string value => String(value),
            _ => Unsupported(literal)
        };

    public static DotBoxdExpressionModel? TryLowerNegative(PrefixUnaryExpressionSyntax unary)
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

    private static DotBoxdExpressionModel Bool(bool value)
        => new(
            $"{DotBoxdGenerationNames.Helpers.Bool}({BoolLiteral(value)})",
            DotBoxdGenerationNames.ManifestTypes.Bool,
            false);

    private static DotBoxdExpressionModel Int32(int value)
        => new(
            $"{DotBoxdGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(value)})",
            DotBoxdGenerationNames.ManifestTypes.Int,
            false);

    private static DotBoxdExpressionModel Int64(long value)
        => new(
            $"{DotBoxdGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxdGenerationNames.ManifestTypes.Long,
            false);

    private static DotBoxdExpressionModel Float64(double value)
        => new(
            $"{DotBoxdGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxdGenerationNames.ManifestTypes.Double,
            false);

    private static DotBoxdExpressionModel String(string value)
        => new(
            $"{DotBoxdGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            DotBoxdGenerationNames.ManifestTypes.String,
            true);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static string BoolLiteral(bool value)
        => value
            ? DotBoxdGenerationNames.CSharpLiterals.True
            : DotBoxdGenerationNames.CSharpLiterals.False;

    private static DotBoxdExpressionModel Unsupported(LiteralExpressionSyntax literal)
        => throw new NotSupportedException($"Unsupported plugin expression '{literal}'.");
}
