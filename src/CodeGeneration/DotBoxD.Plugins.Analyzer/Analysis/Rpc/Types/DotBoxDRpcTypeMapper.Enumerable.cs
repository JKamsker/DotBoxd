using Microsoft.CodeAnalysis;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static partial class DotBoxDRpcTypeMapper
{
    // Non-list/map IEnumerable<T> types expose metadata properties such as Count but not their elements in the
    // record DTO shape. Runtime marshalling rejects them; the analyzer must do the same before generation.
    private static bool ImplementsGenericEnumerable(INamedTypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            if (string.Equals(
                    @interface.OriginalDefinition.ToDisplayString(),
                    TypeNames.EnumerableOriginal,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
