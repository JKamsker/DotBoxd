using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class PluginEventPropertyReader
{
    public static IPropertySymbol[] Read(INamedTypeSymbol eventType)
    {
        var properties = ReadableProperties(eventType);
        ValidatePropertyNames(properties);
        // Declaration order — the single wire-field order. The decoder side (DotBoxDRpcTypeMapper.RecordFields
        // and the runtime KernelRpcMarshaller.GetRecordShape) reads positional record fields in declaration
        // order and reconstructs via the constructor map, so encoder, kernel parameters, and decoder must all
        // agree on declaration order. Reordering to constructor-parameter order here would only match for
        // positional records and silently misalign a non-positional event class whose constructor order differs.
        return properties;
    }

    private static void ValidatePropertyNames(IPropertySymbol[] properties)
    {
        var names = new Dictionary<string, string>(properties.Length, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < properties.Length; i++)
        {
            if (names.TryGetValue(properties[i].Name, out var firstName))
            {
                throw new NotSupportedException(
                    $"Event property '{firstName}' is declared more than once or differs only by case.");
            }

            names.Add(properties[i].Name, properties[i].Name);
        }
    }

    private static IPropertySymbol[] ReadableProperties(INamedTypeSymbol eventType)
    {
        var hierarchy = EventTypeHierarchy(eventType);
        var count = 0;
        for (var i = 0; i < hierarchy.Length; i++)
        {
            foreach (var member in hierarchy[i].GetMembers())
            {
                if (member is IPropertySymbol property && IsReadableProperty(property))
                {
                    count++;
                }
            }
        }

        if (count == 0)
        {
            return Array.Empty<IPropertySymbol>();
        }

        var properties = new IPropertySymbol[count];
        var index = 0;
        for (var i = 0; i < hierarchy.Length; i++)
        {
            foreach (var member in hierarchy[i].GetMembers())
            {
                if (member is IPropertySymbol property && IsReadableProperty(property))
                {
                    properties[index++] = property;
                }
            }
        }

        Array.Sort(properties, (left, right) => ComparePropertyOrder(left, right, hierarchy));
        return properties;
    }

    private static INamedTypeSymbol[] EventTypeHierarchy(INamedTypeSymbol eventType)
    {
        var count = 0;
        for (var current = eventType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            count++;
        }

        if (count == 0)
        {
            return Array.Empty<INamedTypeSymbol>();
        }

        var hierarchy = new INamedTypeSymbol[count];
        var index = count - 1;
        for (var current = eventType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            hierarchy[index] = current;
            index--;
        }

        return hierarchy;
    }

    private static bool IsReadableProperty(IPropertySymbol property)
        => property.DeclaredAccessibility == Accessibility.Public &&
           !property.IsStatic &&
           property.GetMethod?.DeclaredAccessibility == Accessibility.Public &&
           property.Parameters.Length == 0;

    private static int ComparePropertyOrder(
        IPropertySymbol left,
        IPropertySymbol right,
        INamedTypeSymbol[] hierarchy)
    {
        var typeComparison = HierarchyIndex(left, hierarchy).CompareTo(HierarchyIndex(right, hierarchy));
        if (typeComparison != 0)
        {
            return typeComparison;
        }

        var positionComparison = DeclarationPosition(left).CompareTo(DeclarationPosition(right));
        return positionComparison != 0
            ? positionComparison
            : string.Compare(left.MetadataName, right.MetadataName, StringComparison.Ordinal);
    }

    private static int HierarchyIndex(IPropertySymbol property, INamedTypeSymbol[] hierarchy)
    {
        for (var i = 0; i < hierarchy.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(property.ContainingType, hierarchy[i]))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static int DeclarationPosition(IPropertySymbol property)
    {
        if (property.DeclaringSyntaxReferences.Length > 0)
        {
            return property.DeclaringSyntaxReferences[0].Span.Start;
        }

        return property.MetadataToken == 0 ? int.MaxValue : property.MetadataToken;
    }
}
