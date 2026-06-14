namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal static class PluginEventPropertyReader
{
    public static IPropertySymbol[] Read(INamedTypeSymbol eventType)
    {
        var properties = ReadableProperties(eventType);
        ValidatePropertyNames(properties);
        return ConstructorPropertyOrder(eventType, properties) ?? properties;
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
        => property.DeclaringSyntaxReferences.Length == 0
            ? int.MaxValue
            : property.DeclaringSyntaxReferences[0].Span.Start;

    private static IPropertySymbol[]? ConstructorPropertyOrder(
        INamedTypeSymbol eventType,
        IPropertySymbol[] properties)
    {
        foreach (var constructor in eventType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (constructor.Parameters.Length == 0 || constructor.Parameters.Length != properties.Length)
            {
                continue;
            }

            if (MatchesDeclaredPropertyOrder(constructor, properties))
            {
                return properties;
            }

            if (ReorderedConstructorProperties(constructor, properties) is { } reordered)
            {
                return reordered;
            }
        }

        return null;
    }

    private static bool MatchesDeclaredPropertyOrder(IMethodSymbol constructor, IPropertySymbol[] properties)
    {
        for (var i = 0; i < properties.Length; i++)
        {
            var parameter = constructor.Parameters[i];
            var property = properties[i];
            if (!NameMatches(parameter.Name, property.Name) ||
                !SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static IPropertySymbol[]? ReorderedConstructorProperties(
        IMethodSymbol constructor,
        IPropertySymbol[] properties)
    {
        var selected = new IPropertySymbol[properties.Length];
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            var parameter = constructor.Parameters[i];
            var property = FindProperty(properties, parameter);
            if (property is null)
            {
                return null;
            }

            selected[i] = property;
        }

        return selected;
    }

    private static IPropertySymbol? FindProperty(IPropertySymbol[] properties, IParameterSymbol parameter)
    {
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (NameMatches(parameter.Name, property.Name) &&
                SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
            {
                return property;
            }
        }

        return null;
    }

    private static bool NameMatches(string parameterName, string propertyName)
        => string.Equals(parameterName, propertyName, StringComparison.OrdinalIgnoreCase);
}
