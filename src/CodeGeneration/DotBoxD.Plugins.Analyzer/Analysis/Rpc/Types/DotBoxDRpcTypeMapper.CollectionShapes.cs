using Microsoft.CodeAnalysis;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    public static bool IsReadOnlyListShape(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return false;
        }

        var definition = named.ConstructedFrom.ToDisplayString();
        return definition is TypeNames.ReadOnlyListOriginal
            or TypeNames.ReadOnlyCollectionOriginal
            or TypeNames.EnumerableOriginal;
    }

    public static bool IsReadOnlyMapShape(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true } named &&
           named.ConstructedFrom.ToDisplayString() is TypeNames.ReadOnlyDictionaryOriginal;
}
