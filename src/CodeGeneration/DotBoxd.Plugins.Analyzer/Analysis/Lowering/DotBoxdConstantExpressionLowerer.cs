namespace DotBoxd.Plugins.Analyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class DotBoxdConstantExpressionLowerer
{
    public static DotBoxdExpressionModel? TryLower(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => TryLower(expression, semanticModel, cancellationToken, targetType: null);

    public static DotBoxdExpressionModel? TryLower(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? targetType)
    {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (!constant.HasValue)
        {
            return null;
        }

        var type = semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType;
        if (type is null)
        {
            throw new NotSupportedException($"Unsupported plugin constant expression '{expression}'.");
        }

        return Lower(expression, constant.Value, targetType ?? DotBoxdTypeNameReader.SandboxTypeName(type));
    }

    private static DotBoxdExpressionModel Lower(ExpressionSyntax expression, object? value, string type)
        => type switch
        {
            DotBoxdGenerationNames.ManifestTypes.Bool when value is bool boolean => Bool(boolean),
            DotBoxdGenerationNames.ManifestTypes.Int when value is int number => Int32(number),
            DotBoxdGenerationNames.ManifestTypes.Long when value is int number => Int64(number),
            DotBoxdGenerationNames.ManifestTypes.Long when value is long number => Int64(number),
            DotBoxdGenerationNames.ManifestTypes.Double when value is int number => Float64(number),
            DotBoxdGenerationNames.ManifestTypes.Double when value is long number => Float64(number),
            DotBoxdGenerationNames.ManifestTypes.Double when value is double number && IsFinite(number) => Float64(number),
            DotBoxdGenerationNames.ManifestTypes.String when value is string text => String(text),
            _ => throw new NotSupportedException($"Unsupported plugin constant expression '{expression}'.")
        };

    private static DotBoxdExpressionModel Bool(bool value)
        => new(
            $"{DotBoxdGenerationNames.Helpers.Bool}({LiteralReader.ObjectLiteral(value)})",
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

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
