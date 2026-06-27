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

    public static string DateTimeWireJsonType()
        => "{\"name\":\"Record\",\"arguments\":[\"I64\",\"I64\"]}";
}
