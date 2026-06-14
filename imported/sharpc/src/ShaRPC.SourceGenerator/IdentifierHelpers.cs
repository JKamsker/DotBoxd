using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator;

internal static class IdentifierHelpers
{
    public static string EscapeIdentifier(string name)
    {
        var kind = SyntaxFacts.GetKeywordKind(name);
        if (kind == SyntaxKind.None)
        {
            // Contextual keywords (async, await, record, var, ...) return None from GetKeywordKind but
            // still need @-escaping when used as an identifier where the compiler would otherwise parse
            // them as keywords (e.g. a method named 'async' before a return type).
            kind = SyntaxFacts.GetContextualKeywordKind(name);
        }

        return kind == SyntaxKind.None ? name : "@" + name;
    }

    public static string UnescapeIdentifier(string name) =>
        name.StartsWith("@", System.StringComparison.Ordinal)
            ? name.Substring(1)
            : name;

    public static string EscapeNamespace(string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
        {
            return namespaceName;
        }

        return string.Join(".", namespaceName.Split('.').Select(EscapeIdentifier));
    }

    public static string QualifyTypeName(string @namespace, string typeName)
    {
        var escapedTypeName = EscapeIdentifier(typeName);
        if (string.IsNullOrEmpty(@namespace))
        {
            return "global::" + escapedTypeName;
        }

        return "global::" + EscapeNamespace(@namespace) + "." + escapedTypeName;
    }
}
