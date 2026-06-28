using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
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

    public static bool IsFirstClassFrameworkWireStruct(ITypeSymbol type)
        => IsDateTimeWireType(type) ||
           IsTimeSpanWireType(type) ||
           IsDateOnlyWireType(type) ||
           IsTimeOnlyWireType(type) ||
           IsIndexWireType(type) ||
           IsRangeWireType(type);

    public static string DateTimeWireJsonType()
        => "{\"name\":\"Record\",\"arguments\":[\"I64\",\"I64\"]}";

    public static string IndexWireJsonType()
        => "{\"name\":\"Record\",\"arguments\":[\"I32\",\"Bool\"]}";

    public static string RangeWireJsonType()
        => "{\"name\":\"Record\",\"arguments\":[" + IndexWireJsonType() + "," + IndexWireJsonType() + "]}";
}
