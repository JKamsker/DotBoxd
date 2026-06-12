namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrConstantExpressionLowerer
{
    public static SafeIrExpressionModel? TryLower(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => TryLower(expression, semanticModel, cancellationToken, targetType: null);

    public static SafeIrExpressionModel? TryLower(
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

        return Lower(expression, constant.Value, targetType ?? SafeIrTypeNameReader.SandboxTypeName(type));
    }

    private static SafeIrExpressionModel Lower(ExpressionSyntax expression, object? value, string type)
        => type switch
        {
            SafeIrGenerationNames.ManifestTypes.Bool when value is bool boolean => Bool(boolean),
            SafeIrGenerationNames.ManifestTypes.Int when value is int number => Int32(number),
            SafeIrGenerationNames.ManifestTypes.Long when value is int number => Int64(number),
            SafeIrGenerationNames.ManifestTypes.Long when value is long number => Int64(number),
            SafeIrGenerationNames.ManifestTypes.Double when value is int number => Float64(number),
            SafeIrGenerationNames.ManifestTypes.Double when value is long number => Float64(number),
            SafeIrGenerationNames.ManifestTypes.Double when value is double number && IsFinite(number) => Float64(number),
            SafeIrGenerationNames.ManifestTypes.String when value is string text => String(text),
            _ => throw new NotSupportedException($"Unsupported plugin constant expression '{expression}'.")
        };

    private static SafeIrExpressionModel Bool(bool value)
        => new(
            $"{SafeIrGenerationNames.Helpers.Bool}({LiteralReader.ObjectLiteral(value)})",
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

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
