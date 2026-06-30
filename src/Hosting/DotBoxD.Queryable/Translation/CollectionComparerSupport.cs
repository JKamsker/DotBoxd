using System.Collections.ObjectModel;
using System.Reflection;

namespace DotBoxD.Queryable.Translation;

internal static class CollectionComparerSupport
{
    public static bool HasUnsupportedComparer(object collection)
    {
        var comparer = GetComparer(collection, depth: 0);
        if (comparer is null)
        {
            return false;
        }

        // Behavioral probes rather than identity checks against public singletons: an ordinal/default string
        // comparer treats these pairs as distinct. Case-insensitive comparers match "a"/"A"; culture-sensitive
        // case-sensitive comparers can match "a\0"/"a" because some cultures ignore embedded nulls.
        // SortedSet<T> exposes ordering comparers, so compare equality must be checked there too.
        if (comparer is IEqualityComparer<string> equalityComparer)
        {
            return equalityComparer.Equals("a", "A") || equalityComparer.Equals("a\0", "a");
        }

        if (comparer is IComparer<string> orderingComparer)
        {
            return orderingComparer.Compare("a", "A") == 0 || orderingComparer.Compare("a\0", "a") == 0;
        }

        return HasCustomGenericComparer(comparer);
    }

    private static bool HasCustomGenericComparer(object comparer)
    {
        foreach (var interfaceType in comparer.GetType().GetInterfaces())
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var interfaceDefinition = interfaceType.GetGenericTypeDefinition();
            var elementType = interfaceType.GetGenericArguments()[0];
            if (elementType == typeof(string))
            {
                continue;
            }

            if (interfaceDefinition == typeof(IEqualityComparer<>))
            {
                return !IsDefaultComparer(comparer, typeof(EqualityComparer<>), elementType);
            }

            if (interfaceDefinition == typeof(IComparer<>))
            {
                return !IsDefaultComparer(comparer, typeof(Comparer<>), elementType);
            }
        }

        return false;
    }

    private static bool IsDefaultComparer(object comparer, Type openComparerType, Type elementType)
    {
        var defaultComparer = openComparerType
            .MakeGenericType(elementType)
            .GetProperty(nameof(EqualityComparer<int>.Default))
            ?.GetValue(null);
        return ReferenceEquals(comparer, defaultComparer);
    }

    private static object? GetComparer(object collection, int depth)
    {
        var type = collection.GetType();
        var comparer = type.GetProperty("Comparer")?.GetValue(collection);
        if (comparer is not null)
        {
            return comparer;
        }

        if (depth >= 3)
        {
            return null;
        }

        if (!string.Equals(type.Name, "KeyCollection", StringComparison.Ordinal) ||
            type.DeclaringType is not { IsGenericType: true } declaringType ||
            !IsDictionaryKeyCollection(declaringType.GetGenericTypeDefinition()))
        {
            return null;
        }

        // Dictionary key views preserve their owner's comparer but do not expose it publicly.
        return GetComparerFromField(collection, "_dictionary", depth) ??
            GetComparerFromField(collection, "_collection", depth);
    }

    private static object? GetComparerFromField(object collection, string fieldName, int depth)
    {
        var inner = collection.GetType()
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(collection);
        return inner is null ? null : GetComparer(inner, depth + 1);
    }

    private static bool IsDictionaryKeyCollection(Type type)
        => type == typeof(Dictionary<,>) ||
            type == typeof(SortedDictionary<,>) ||
            type == typeof(ReadOnlyDictionary<,>);
}
