using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcDtoFieldMatcher
{
    public static int FieldIndex(
        IReadOnlyList<IPropertySymbol> fields,
        IParameterSymbol parameter)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Name, parameter.Name, StringComparison.Ordinal))
            {
                continue;
            }

            return SymbolEqualityComparer.Default.Equals(fields[i].Type, parameter.Type) ? i : -1;
        }

        var match = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Name, parameter.Name, StringComparison.OrdinalIgnoreCase) ||
                !SymbolEqualityComparer.Default.Equals(fields[i].Type, parameter.Type))
            {
                continue;
            }

            if (match >= 0)
            {
                return -1;
            }

            match = i;
        }

        return match;
    }
}
