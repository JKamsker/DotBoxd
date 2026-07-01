using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class KernelMethodDefaultArgumentLowerer
{
    public static DotBoxDExpressionModel Lower(IParameterSymbol parameter, object? value)
    {
        if (DotBoxDNullableScalarType.TryGetSupportedUnderlying(parameter.Type, out _))
        {
            return LowerNullableDefaultArgument(parameter, value);
        }

        return LowerScalarDefaultArgument(parameter.Type, value, parameter);
    }

    private static DotBoxDExpressionModel LowerNullableDefaultArgument(IParameterSymbol parameter, object? value)
    {
        if (value is null)
        {
            return new(
                DotBoxDNullableScalarExpressionLowerer.NullSource(parameter.Type),
                DotBoxDGenerationNames.ManifestTypes.Record,
                true);
        }

        var scalar = LowerScalarDefaultArgument(
            ((INamedTypeSymbol)parameter.Type).TypeArguments[0],
            value,
            parameter);
        return new(
            DotBoxDNullableScalarExpressionLowerer.PresentSource(parameter.Type, scalar),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);
    }

    private static DotBoxDExpressionModel LowerScalarDefaultArgument(
        ITypeSymbol parameterType,
        object? value,
        IParameterSymbol parameter)
    {
        var type = DotBoxDTypeNameReader.KernelMethodTypeName(parameterType);
        if (parameterType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType && value is not null)
        {
            var raw = EnumDefaultValue(enumType, value);
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(raw)})",
                    type,
                    false)
                : new DotBoxDExpressionModel(
                    $"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(unchecked((int)raw))})",
                    type,
                    false);
        }

        if (parameterType.SpecialType == SpecialType.System_DateTime && value is DateTime dateTime)
        {
            return LowerDateTimeDefault(parameterType, dateTime, parameter);
        }

        if (DotBoxDRpcTypeMapper.IsGuid(parameterType) &&
            value is Guid guid &&
            guid == Guid.Empty &&
            TryLowerFrameworkDefault(parameterType) is { } guidDefault)
        {
            return guidDefault;
        }

        if (value is null && TryLowerFrameworkDefault(parameterType) is { } frameworkDefault)
        {
            return frameworkDefault;
        }

        return type switch
        {
            DotBoxDGenerationNames.ManifestTypes.Bool when value is bool boolean => new(
                $"{DotBoxDGenerationNames.Helpers.Bool}({LiteralReader.ObjectLiteral(boolean)})",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                false),
            DotBoxDGenerationNames.ManifestTypes.Int when value is int number => new(
                $"{DotBoxDGenerationNames.Helpers.I32}({LiteralReader.ObjectLiteral(number)})",
                DotBoxDGenerationNames.ManifestTypes.Int,
                false),
            DotBoxDGenerationNames.ManifestTypes.Long when value is int number => new(
                $"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral((long)number)})",
                DotBoxDGenerationNames.ManifestTypes.Long,
                false),
            DotBoxDGenerationNames.ManifestTypes.Long when value is long number => new(
                $"{DotBoxDGenerationNames.Helpers.I64}({LiteralReader.ObjectLiteral(number)})",
                DotBoxDGenerationNames.ManifestTypes.Long,
                false),
            DotBoxDGenerationNames.ManifestTypes.Double when value is int number => new(
                $"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral((double)number)})",
                DotBoxDGenerationNames.ManifestTypes.Double,
                false),
            DotBoxDGenerationNames.ManifestTypes.Double when value is long number => new(
                $"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral((double)number)})",
                DotBoxDGenerationNames.ManifestTypes.Double,
                false),
            DotBoxDGenerationNames.ManifestTypes.Double when value is float number && IsFinite(number) => new(
                $"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral((double)number)})",
                DotBoxDGenerationNames.ManifestTypes.Double,
                false),
            DotBoxDGenerationNames.ManifestTypes.Double when value is double number && IsFinite(number) => new(
                $"{DotBoxDGenerationNames.Helpers.F64}({LiteralReader.ObjectLiteral(number)})",
                DotBoxDGenerationNames.ManifestTypes.Double,
                false),
            DotBoxDGenerationNames.ManifestTypes.String when value is string text => new(
                $"{DotBoxDGenerationNames.Helpers.Str}({LiteralReader.StringLiteral(text)})",
                DotBoxDGenerationNames.ManifestTypes.String,
                true),
            _ => throw new NotSupportedException(
                $"[KernelMethod] '{parameter.ContainingSymbol.Name}' optional parameter '{parameter.Name}' has an unsupported default value.")
        };
    }

    private static DotBoxDExpressionModel LowerDateTimeDefault(
        ITypeSymbol type,
        DateTime value,
        IParameterSymbol parameter)
    {
        if (value.Kind != DateTimeKind.Unspecified)
        {
            throw new NotSupportedException(
                $"[KernelMethod] '{parameter.ContainingSymbol.Name}' optional parameter '{parameter.Name}' DateTime default must use DateTimeKind.Unspecified.");
        }

        return new(
            DateTimeRecord(type, LiteralReader.ObjectLiteral(value.Ticks), DotBoxDGenerationNames.CSharpLiterals.Int64Default),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);
    }

    private static DotBoxDExpressionModel? TryLowerFrameworkDefault(ITypeSymbol type)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return new(
                $"new {DotBoxDGenerationNames.TypeNames.GlobalLiteralExpression}({DotBoxDGenerationNames.TypeNames.GlobalSandboxValue}.FromGuid(global::System.Guid.Empty), Span)",
                DotBoxDGenerationNames.ManifestTypes.Guid,
                false);
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return new(DateTimeRecordDefault(type), DotBoxDGenerationNames.ManifestTypes.Record, true);
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
            return new(
                $"{DotBoxDGenerationNames.Helpers.I32}({DotBoxDGenerationNames.CSharpLiterals.Int32Default})",
                DotBoxDGenerationNames.ManifestTypes.Int,
                false);

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type) || DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
            return new(
                $"{DotBoxDGenerationNames.Helpers.I64}({DotBoxDGenerationNames.CSharpLiterals.Int64Default})",
                DotBoxDGenerationNames.ManifestTypes.Long,
                false);

        return null;
    }

    private static string DateTimeRecordDefault(ITypeSymbol type)
        => DateTimeRecord(
            type,
            DotBoxDGenerationNames.CSharpLiterals.Int64Default,
            DotBoxDGenerationNames.CSharpLiterals.Int64Default);

    private static string DateTimeRecord(ITypeSymbol type, string utcTicks, string offsetTicks)
        => DotBoxDRecordCreationExpressionLowerer.RecordNew(
            [
                $"{DotBoxDGenerationNames.Helpers.I64}({utcTicks})",
                $"{DotBoxDGenerationNames.Helpers.I64}({offsetTicks})"
            ],
            SandboxTypeSourceEmitter.TryEmit(type) ?? throw new NotSupportedException());

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static long EnumDefaultValue(INamedTypeSymbol enumType, object value)
        => enumType.EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_UInt64 => unchecked((long)(ulong)value),
            SpecialType.System_UInt32 => (uint)value,
            SpecialType.System_Int64 => (long)value,
            SpecialType.System_Int32 => (int)value,
            SpecialType.System_UInt16 => (ushort)value,
            SpecialType.System_Int16 => (short)value,
            SpecialType.System_Byte => (byte)value,
            SpecialType.System_SByte => (sbyte)value,
            _ => throw new NotSupportedException(
                $"[KernelMethod] '{enumType.ToDisplayString()}' enum default value is not supported.")
        };
}
