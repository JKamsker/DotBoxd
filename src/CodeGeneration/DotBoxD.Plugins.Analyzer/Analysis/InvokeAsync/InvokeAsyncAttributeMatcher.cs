using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal static class InvokeAsyncAttributeMatcher
{
    public static bool HasGeneratePluginServerAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDMetadataNames.GeneratePluginServerAttribute);

    public static bool HasRpcServiceAttribute(INamedTypeSymbol type)
        => HasAttribute(type, DotBoxDMetadataNames.RpcServiceAttribute);

    private static bool HasAttribute(INamedTypeSymbol type, string metadataName)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            var matches = string.Equals(metadataName, DotBoxDMetadataNames.RpcServiceAttribute, StringComparison.Ordinal)
                ? DotBoxDMetadataNames.IsRpcServiceAttribute(attributeName)
                : string.Equals(attributeName, metadataName, StringComparison.Ordinal);
            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}
