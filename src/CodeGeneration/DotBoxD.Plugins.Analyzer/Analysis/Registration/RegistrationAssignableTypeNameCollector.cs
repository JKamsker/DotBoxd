namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class RegistrationAssignableTypeNameCollector
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat;

    public static EquatableArray<string> Collect(ITypeSymbol type)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Add(names, seen, type);

        if (type is INamedTypeSymbol named)
        {
            AddBaseTypes(names, seen, named);
            AddInterfaces(names, seen, named);
        }

        return EquatableArray<string>.FromOwned(names.ToArray());
    }

    private static void AddBaseTypes(
        List<string> names,
        HashSet<string> seen,
        INamedTypeSymbol type)
    {
        for (var current = type.BaseType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            Add(names, seen, current);
        }
    }

    private static void AddInterfaces(
        List<string> names,
        HashSet<string> seen,
        INamedTypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            Add(names, seen, @interface);
        }
    }

    private static void Add(List<string> names, HashSet<string> seen, ITypeSymbol type)
    {
        var name = TypeName(type);
        if (seen.Add(name))
        {
            names.Add(name);
        }
    }

    private static string TypeName(ITypeSymbol type)
        => type is ITypeParameterSymbol parameter
            ? Identifier(parameter.Name)
            : type.ToDisplayString(FullyQualifiedFormat);

    private static string Identifier(string name)
    {
        var kind = SyntaxFacts.GetKeywordKind(name);
        if (kind == SyntaxKind.None)
        {
            kind = SyntaxFacts.GetContextualKeywordKind(name);
        }

        return kind == SyntaxKind.None ? name : "@" + name;
    }
}
