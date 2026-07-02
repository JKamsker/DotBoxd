using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDConstantExpressionLowerer
{
    public static DotBoxDExpressionModel? TryLower(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => TryLower(expression, semanticModel, cancellationToken, targetType: null);

    public static DotBoxDExpressionModel? TryLower(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? targetType)
    {
        if (TryLowerDefaultValue(expression, semanticModel, cancellationToken, targetType) is { } defaultValue)
        {
            return defaultValue;
        }

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

        // An enum constant (e.g. GamePhase.Battle) lowers to its underlying integer literal — the same I32/I64
        // representation an enum event property or enum DTO field carries (by underlying width). Convert handles
        // narrow (byte/short/…) and wide (uint/long/ulong) backing types. Only applied when the caller imposes no
        // explicit target type. This is what lets `e.Phase == GamePhase.Battle` filters and
        // `Select(e => new Dto(e.Id, GamePhase.Battle))` projections lower.
        if (targetType is null && type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? Int64(EnumConstantToInt64(constant.Value, enumType))
                : Int32(Convert.ToInt32(constant.Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        return Lower(expression, constant.Value, targetType ?? DotBoxDTypeNameReader.SandboxTypeName(type));
    }

    private static DotBoxDExpressionModel Lower(ExpressionSyntax expression, object? value, string type)
        => type switch
        {
            DotBoxDGenerationNames.ManifestTypes.Bool when value is bool boolean => Bool(boolean),
            DotBoxDGenerationNames.ManifestTypes.Int when value is int number => Int32(number),
            DotBoxDGenerationNames.ManifestTypes.Long when value is int number => Int64(number),
            DotBoxDGenerationNames.ManifestTypes.Long when value is long number => Int64(number),
            DotBoxDGenerationNames.ManifestTypes.Double when value is int number => Float64(number),
            DotBoxDGenerationNames.ManifestTypes.Double when value is long number => Float64(number),
            DotBoxDGenerationNames.ManifestTypes.Double when value is float number && IsFinite(number) => Float64(number),
            DotBoxDGenerationNames.ManifestTypes.Double when value is double number && IsFinite(number) => Float64(number),
            DotBoxDGenerationNames.ManifestTypes.String when value is string text => String(text),
            _ => throw new NotSupportedException($"Unsupported plugin constant expression '{expression}'.")
        };

    private static DotBoxDExpressionModel? TryLowerDefaultValue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        string? targetType)
    {
        if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
            expression is not DefaultExpressionSyntax)
        {
            return null;
        }

        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        var type = typeInfo.ConvertedType ?? typeInfo.Type;
        if (type is null)
        {
            return null;
        }

        var manifestTag = SandboxTypeSourceEmitter.ManifestTag(type);
        if (targetType is not null && !string.Equals(targetType, manifestTag, StringComparison.Ordinal))
        {
            return null;
        }

        return LowerDefault(type, manifestTag);
    }

    private static DotBoxDExpressionModel? LowerDefault(ITypeSymbol type, string manifestTag)
        => manifestTag switch
        {
            DotBoxDGenerationNames.ManifestTypes.Guid when DotBoxDRpcTypeMapper.IsGuid(type) => GuidDefault(),
            DotBoxDGenerationNames.ManifestTypes.Record when DotBoxDRpcTypeMapper.IsDateTimeWireType(type) =>
                DateTimeRecordDefault(type),
            DotBoxDGenerationNames.ManifestTypes.Int when DotBoxDRpcTypeMapper.IsDateOnlyWireType(type) =>
                Int32(0),
            DotBoxDGenerationNames.ManifestTypes.Long when
                DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type) ||
                DotBoxDRpcTypeMapper.IsTimeSpanWireType(type) => Int64(0),
            _ => null
        };

    private static DotBoxDExpressionModel DateTimeRecordDefault(ITypeSymbol type)
        => new(
            DotBoxDRecordCreationExpressionLowerer.RecordNew(
                [
                    $"{DotBoxDGenerationNames.Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
                    $"{DotBoxDGenerationNames.Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})"
                ],
            SandboxTypeSourceEmitter.TryEmit(type) ?? throw new NotSupportedException()),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);

    private static DotBoxDExpressionModel Bool(bool value)
        => new(
            $"{DotBoxDGenerationNames.Helpers.Bool}({LiteralReader.ObjectLiteral(value)})",
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

    // A ulong-backed enum value above long.MaxValue overflows a range-checked Convert.ToInt64; reinterpret its
    // bits instead so the value carries losslessly (the decoder casts the I64 back to the enum, also unchecked).
    private static long EnumConstantToInt64(object? value, INamedTypeSymbol enumType)
        => enumType.EnumUnderlyingType?.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_UInt64
            ? unchecked((long)Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture))
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

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

    private static DotBoxDExpressionModel GuidDefault()
        => new(
            $"new {DotBoxDGenerationNames.TypeNames.GlobalLiteralExpression}({DotBoxDGenerationNames.TypeNames.GlobalSandboxValue}.FromGuid(global::System.Guid.Empty), Span)",
            DotBoxDGenerationNames.ManifestTypes.Guid,
            false);

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
