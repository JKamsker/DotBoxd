namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;

internal static class RpcDtoFieldMatcher
{
    public static void ValidateNoRefLikeParameters(IMethodSymbol constructor, string owner)
    {
        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                throw new NotSupportedException(
                    $"{owner} constructor '{constructor.ToDisplayString()}' must not declare ref, out, or in parameters.");
            }
        }
    }

    public static int FieldIndex(
        IReadOnlyList<RecordMember> fields,
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

    public static string DefaultConstructorArgument(IParameterSymbol parameter)
        => string.Concat(
            "@",
            parameter.Name,
            ": ",
            LiteralReader.ObjectDefaultLiteral(parameter.Type, parameter.ExplicitDefaultValue));
}
