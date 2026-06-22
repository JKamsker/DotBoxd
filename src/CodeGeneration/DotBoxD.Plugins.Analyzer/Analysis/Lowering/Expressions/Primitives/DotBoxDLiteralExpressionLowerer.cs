using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDLiteralExpressionLowerer
{
    public static DotBoxDExpressionModel Lower(LiteralExpressionSyntax literal)
        => literal.Token.Value switch
        {
            bool value => Bool(value),
            int value => Int32(value),
            long value => Int64(value),
            double value when IsFinite(value) => Float64(value),
            string value => String(value),
            _ => Unsupported(literal)
        };

    public static DotBoxDExpressionModel? TryLowerNegative(PrefixUnaryExpressionSyntax unary)
    {
        if (unary.Kind() != SyntaxKind.UnaryMinusExpression ||
            unary.Operand is not LiteralExpressionSyntax literal)
        {
            return null;
        }

        return literal.Token.Value switch
        {
            int value when value != int.MinValue => Int32(-value),
            long value when value != long.MinValue => Int64(-value),
            double value when IsFinite(value) => Float64(-value),
            _ => null
        };
    }

    private static DotBoxDExpressionModel Bool(bool value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Bool}({BoolLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Bool,
            false);

    private static DotBoxDExpressionModel Int32(int value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Int,
            false);

    private static DotBoxDExpressionModel Int64(long value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Long,
            false);

    private static DotBoxDExpressionModel Float64(double value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.Double,
            false);

    private static DotBoxDExpressionModel String(string value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(value)})",
            DotBoxDGenerationNames.ManifestTypes.String,
            true);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static string BoolLiteral(bool value)
        => value
            ? DotBoxDGenerationNames.CSharpLiterals.True
            : DotBoxDGenerationNames.CSharpLiterals.False;

    private static DotBoxDExpressionModel Unsupported(LiteralExpressionSyntax literal)
        => throw new NotSupportedException($"Unsupported plugin expression '{literal}'.");
}
