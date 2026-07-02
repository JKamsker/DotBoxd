using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    public static bool IsScalar(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32
            or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_Single
            or SpecialType.System_String ||
           IsDateOnlyWireType(type) ||
           IsTimeOnlyWireType(type) ||
           IsTimeSpanWireType(type) ||
           IsCancellationTokenWireType(type);

    /// <summary>
    /// <see cref="System.Guid"/> is a first-class 16-byte scalar (sandbox <c>Guid</c> kind), distinct from
    /// <c>string</c>. Detected structurally so it is robust to display-format differences.
    /// </summary>
    public static bool IsGuid(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "Guid", ContainingNamespace: { Name: "System" } ns }
           && ns.ContainingNamespace is { IsGlobalNamespace: true };

    /// <summary>
    /// A map key must lower to a scalar the kernel verifier accepts as a key: <c>bool</c>, <c>int</c>,
    /// <c>long</c>, <c>string</c>, <c>DateOnly</c>, <c>TimeOnly</c>, <c>TimeSpan</c>, or an enum.
    /// </summary>
    public static bool IsSupportedMapKey(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32
               or SpecialType.System_Int64 or SpecialType.System_String
           || type.TypeKind == TypeKind.Enum
           || IsDateOnlyWireType(type)
           || IsTimeOnlyWireType(type)
           || IsTimeSpanWireType(type);

    /// <summary>
    /// An enum marshals through its underlying integer; widths that overflow <c>I32</c> (<c>uint</c>,
    /// <c>long</c>, <c>ulong</c>) use <c>I64</c>, everything else <c>I32</c>.
    /// </summary>
    public static bool EnumUsesI64(INamedTypeSymbol enumType)
        => enumType.EnumUnderlyingType?.SpecialType is
            SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;

    public static bool IsDateTimeWireType(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "DateTime" or "DateTimeOffset", ContainingNamespace: { Name: "System" } ns } &&
           ns.ContainingNamespace is { IsGlobalNamespace: true };

    public static bool IsTimeSpanWireType(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "TimeSpan", ContainingNamespace: { Name: "System" } ns } &&
           ns.ContainingNamespace is { IsGlobalNamespace: true };

    public static bool IsDateOnlyWireType(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "DateOnly", ContainingNamespace: { Name: "System" } ns } &&
           ns.ContainingNamespace is { IsGlobalNamespace: true };

    public static bool IsTimeOnlyWireType(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "TimeOnly", ContainingNamespace: { Name: "System" } ns } &&
           ns.ContainingNamespace is { IsGlobalNamespace: true };

    public static bool IsIndexWireType(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "Index", ContainingNamespace: { Name: "System" } ns } &&
           ns.ContainingNamespace is { IsGlobalNamespace: true };

    public static bool IsRangeWireType(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "Range", ContainingNamespace: { Name: "System" } ns } &&
           ns.ContainingNamespace is { IsGlobalNamespace: true };

    public static bool IsCancellationTokenWireType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { Name: "CancellationToken" } named)
        {
            return false;
        }

        var ns = named.ContainingNamespace;
        return ns.Name == "Threading" &&
               ns.ContainingNamespace.Name == "System" &&
               ns.ContainingNamespace.ContainingNamespace.IsGlobalNamespace;
    }

    public static bool IsFirstClassFrameworkWireStruct(ITypeSymbol type)
        => IsDateTimeWireType(type) ||
           IsTimeSpanWireType(type) ||
           IsDateOnlyWireType(type) ||
           IsTimeOnlyWireType(type) ||
           IsIndexWireType(type) ||
           IsRangeWireType(type) ||
           IsCancellationTokenWireType(type);

    public static string DateTimeWireJsonType()
        => "{\"name\":\"Record\",\"arguments\":[\"I64\",\"I64\"]}";

    public static string IndexWireJsonType()
        => "{\"name\":\"Record\",\"arguments\":[\"I32\",\"Bool\"]}";

    public static string RangeWireJsonType()
        => "{\"name\":\"Record\",\"arguments\":[" + IndexWireJsonType() + "," + IndexWireJsonType() + "]}";
}
