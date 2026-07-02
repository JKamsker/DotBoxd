using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Helpers = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.Helpers;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDNullableScalarExpressionLowerer
{
    public static bool TryLower(
        ExpressionSyntax expression,
        ITypeSymbol targetType,
        DotBoxDExpressionLoweringContext context,
        Func<ExpressionSyntax, DotBoxDExpressionModel> lowerExpression,
        out DotBoxDExpressionModel lowered)
    {
        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(targetType, out var underlying))
        {
            lowered = null!;
            return false;
        }

        if (IsNullLike(expression, context))
        {
            lowered = Null(targetType, underlying);
            return true;
        }

        if (DotBoxDConstantExpressionLowerer.TryLower(
                expression,
                context.SemanticModel,
                context.CancellationToken,
                SandboxTypeSourceEmitter.ManifestTag(underlying)) is { } constant)
        {
            lowered = Present(targetType, underlying, constant);
            return true;
        }

        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        if (typeInfo.Type is { } expressionType &&
            DotBoxDNullableScalarType.TryGetSupportedUnderlying(expressionType, out var expressionUnderlying) &&
            SymbolEqualityComparer.Default.Equals(expressionUnderlying, underlying))
        {
            lowered = lowerExpression(expression);
            RequireTag(lowered, DotBoxDGenerationNames.ManifestTypes.Record);
            return true;
        }

        var value = lowerExpression(expression);
        RequireTag(value, SandboxTypeSourceEmitter.ManifestTag(underlying));
        lowered = Present(targetType, underlying, value);
        return true;
    }

    public static string NullSource(ITypeSymbol nullableType)
    {
        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(nullableType, out var underlying))
        {
            throw new NotSupportedException();
        }

        return Null(nullableType, underlying).Source;
    }

    public static string PresentSource(ITypeSymbol nullableType, DotBoxDExpressionModel value)
    {
        if (!DotBoxDNullableScalarType.TryGetSupportedUnderlying(nullableType, out var underlying))
        {
            throw new NotSupportedException();
        }

        RequireTag(value, SandboxTypeSourceEmitter.ManifestTag(underlying));
        return Present(nullableType, underlying, value).Source;
    }

    private static DotBoxDExpressionModel Null(ITypeSymbol nullableType, ITypeSymbol underlying)
        => new(RecordSource(nullableType, BoolSource(value: false), ZeroSource(underlying)), DotBoxDGenerationNames.ManifestTypes.Record, true);

    private static DotBoxDExpressionModel Present(
        ITypeSymbol nullableType,
        ITypeSymbol underlying,
        DotBoxDExpressionModel value)
        => new(
            RecordSource(nullableType, BoolSource(value: true), value.Source),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);

    private static string RecordSource(ITypeSymbol nullableType, string hasValue, string value)
        => DotBoxDRecordCreationExpressionLowerer.RecordNew(
            [hasValue, value],
            SandboxTypeSourceEmitter.TryEmit(nullableType) ?? throw new NotSupportedException());

    private static bool IsNullLike(ExpressionSyntax expression, DotBoxDExpressionLoweringContext context)
    {
        if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
            IsNullableDefaultExpression(expression, context))
        {
            return true;
        }

        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        return constant.HasValue && constant.Value is null;
    }

    private static bool IsNullableDefaultExpression(
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
    {
        if (!expression.IsKind(SyntaxKind.DefaultExpression))
        {
            return false;
        }

        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type is not null && DotBoxDNullableScalarType.IsNullableValueType(type);
    }

    private static string ZeroSource(ITypeSymbol underlying)
        => underlying.SpecialType switch
        {
            SpecialType.System_Boolean => BoolSource(value: false),
            SpecialType.System_Int32 => $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
            SpecialType.System_Int64 => $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
            SpecialType.System_Double or SpecialType.System_Single =>
                $"{Helpers.F64}({DotBoxDGenerationNames.CSharpLiterals.DoubleDefault})",
            _ when DotBoxDRpcTypeMapper.IsGuid(underlying) =>
                $"new {TypeNames.GlobalLiteralExpression}({TypeNames.GlobalSandboxValue}.FromGuid(global::System.Guid.Empty), Span)",
            _ when DotBoxDRpcTypeMapper.IsDecimalWireType(underlying) => DecimalZeroSource(underlying),
            _ when DotBoxDRpcTypeMapper.IsDateOnlyWireType(underlying) =>
                $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
            _ when DotBoxDRpcTypeMapper.IsTimeOnlyWireType(underlying) ||
                   DotBoxDRpcTypeMapper.IsTimeSpanWireType(underlying) =>
                $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
            _ when DotBoxDRpcTypeMapper.IsCancellationTokenWireType(underlying) =>
                BoolSource(value: false),
            _ when underlying.TypeKind == TypeKind.Enum && underlying is INamedTypeSymbol enumType =>
                DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                    ? $"{Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})"
                    : $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
            _ => throw new NotSupportedException()
        };

    private static string DecimalZeroSource(ITypeSymbol underlying)
        => DotBoxDRecordCreationExpressionLowerer.RecordNew(
            [
                $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
                $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
                $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
                $"{Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})"
            ],
            SandboxTypeSourceEmitter.TryEmit(underlying) ?? throw new NotSupportedException());

    private static string BoolSource(bool value)
        => $"{Helpers.Bool}({(value ? DotBoxDGenerationNames.CSharpLiterals.True : DotBoxDGenerationNames.CSharpLiterals.False)})";

    private static void RequireTag(DotBoxDExpressionModel expression, string expected)
    {
        if (!string.Equals(expression.Type, expected, StringComparison.Ordinal))
        {
            throw new NotSupportedException();
        }
    }
}
