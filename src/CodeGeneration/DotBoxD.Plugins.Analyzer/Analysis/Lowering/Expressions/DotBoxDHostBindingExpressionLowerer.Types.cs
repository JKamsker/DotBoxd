using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class DotBoxDHostBindingExpressionLowerer
{
    private static string HostBindingManifestTag(ITypeSymbol type, string bindingId, string position)
    {
        if (ContainsNullableValueType(type, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)))
        {
            throw new NotSupportedException(
                $"Host binding '{bindingId}' {position} type must not contain nullable value types.");
        }

        var tag = SandboxTypeSourceEmitter.ManifestTag(type);
        if (string.Equals(tag, ManifestTypes.Unsupported, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Host binding '{bindingId}' {position} type must be marshaller-eligible.");
        }

        return tag;
    }

    private static bool ContainsNullableValueType(ITypeSymbol type, HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (DotBoxDNullableScalarType.IsNullableValueType(type))
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return array.Rank == 1 && ContainsNullableValueType(array.ElementType, visited);
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType &&
            ContainsNullableValueType(elementType, visited))
        {
            return true;
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map &&
            (ContainsNullableValueType(map.Key, visited) ||
             ContainsNullableValueType(map.Value, visited)))
        {
            return true;
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            foreach (var field in DotBoxDRpcTypeMapper.RecordFields(named))
            {
                if (ContainsNullableValueType(field.Type, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
