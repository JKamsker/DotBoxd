using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class DotBoxDNullableScalarType
{
    public static bool TryGetSupportedUnderlying(ITypeSymbol type, out ITypeSymbol underlying)
    {
        if (type is INamedTypeSymbol named &&
            named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T &&
            IsSupportedUnderlying(named.TypeArguments[0]))
        {
            underlying = named.TypeArguments[0];
            return true;
        }

        underlying = null!;
        return false;
    }

    public static bool IsNullableValueType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T;

    public static bool IsSupportedUnderlying(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Boolean
               or SpecialType.System_Int32
               or SpecialType.System_Int64
               or SpecialType.System_Double
               or SpecialType.System_Single
           || type.TypeKind == TypeKind.Enum
           || DotBoxDRpcTypeMapper.IsGuid(type)
           || DotBoxDRpcTypeMapper.IsDateTimeWireType(type)
           || DotBoxDRpcTypeMapper.IsDateOnlyWireType(type)
           || DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type)
           || DotBoxDRpcTypeMapper.IsTimeSpanWireType(type)
           || DotBoxDRpcTypeMapper.IsCancellationTokenWireType(type);
}
