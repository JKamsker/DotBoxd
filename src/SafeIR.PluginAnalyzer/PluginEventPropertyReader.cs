namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis;

internal static class PluginEventPropertyReader
{
    public static IReadOnlyList<IPropertySymbol> Read(INamedTypeSymbol eventType)
    {
        var properties = ReadableProperties(eventType).ToArray();
        ValidatePropertyNames(properties);
        return ConstructorPropertyOrder(eventType, properties) ?? properties;
    }

    private static void ValidatePropertyNames(IPropertySymbol[] properties)
    {
        var duplicate = properties
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new NotSupportedException(
                $"Event property '{duplicate.First().Name}' is declared more than once or differs only by case.");
        }
    }

    private static IEnumerable<IPropertySymbol> ReadableProperties(INamedTypeSymbol eventType)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = eventType;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            hierarchy.Push(current);
        }

        while (hierarchy.Count > 0)
        {
            var current = hierarchy.Pop();
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (IsReadableProperty(property))
                {
                    yield return property;
                }
            }
        }
    }

    private static bool IsReadableProperty(IPropertySymbol property)
        => property.DeclaredAccessibility == Accessibility.Public &&
           !property.IsStatic &&
           property.GetMethod is not null &&
           property.Parameters.Length == 0;

    private static IPropertySymbol[]? ConstructorPropertyOrder(
        INamedTypeSymbol eventType,
        IPropertySymbol[] properties)
    {
        var byName = properties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var constructor in eventType.InstanceConstructors.Where(c => c.DeclaredAccessibility == Accessibility.Public))
        {
            if (constructor.Parameters.Length == 0 || constructor.Parameters.Length != properties.Length)
            {
                continue;
            }

            var selected = new IPropertySymbol[constructor.Parameters.Length];
            var matched = true;
            for (var i = 0; i < constructor.Parameters.Length; i++)
            {
                var parameter = constructor.Parameters[i];
                if (!byName.TryGetValue(parameter.Name, out var property) ||
                    !SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
                {
                    matched = false;
                    break;
                }

                selected[i] = property;
            }

            if (matched)
            {
                return selected;
            }
        }

        return null;
    }
}
