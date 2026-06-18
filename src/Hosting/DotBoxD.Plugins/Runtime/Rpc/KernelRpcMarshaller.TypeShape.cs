using System.Collections.Concurrent;
using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly ConcurrentDictionary<Type, OptionalType> ElementTypeCache = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>> RecordFieldCache = new();

    private static Type? ElementType(Type type)
        => ElementTypeCache.GetOrAdd(type, static candidate => new OptionalType(FindElementType(candidate))).Value;

    private static Type? FindElementType(Type type)
    {
        if (type.IsArray)
        {
            if (type.GetArrayRank() != 1)
            {
                throw new NotSupportedException(
                    $"Kernel RPC service cannot marshal multidimensional array type '{type}'.");
            }

            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IList<>) || definition == typeof(IEnumerable<>) ||
                definition == typeof(IReadOnlyCollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static bool IsDto(Type type)
        => type != typeof(string) &&
           !type.IsPrimitive &&
           !type.IsEnum &&
           ElementType(type) is null &&
           (type.IsClass || type.IsValueType) &&
           RecordFields(type).Count > 0;

    private static IReadOnlyList<PropertyInfo> RecordFields(Type type)
        => RecordFieldCache.GetOrAdd(type, static candidate =>
        {
            var properties = new List<PropertyInfo>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var property in candidate.GetProperties(flags))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal))
                {
                    properties.Add(property);
                }
            }

            properties.Sort(static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            return properties;
        });

    private static object Construct(Type type, IReadOnlyList<PropertyInfo> fields, object?[] arguments)
    {
        foreach (var constructor in type.GetConstructors())
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length != fields.Count || parameters.Length == 0)
            {
                continue;
            }

            var ordered = new object?[parameters.Length];
            var assigned = new bool[parameters.Length];
            var matched = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = FieldIndex(fields, parameters[i].Name);
                if (fieldIndex < 0 ||
                    assigned[fieldIndex] ||
                    parameters[i].ParameterType != fields[fieldIndex].PropertyType)
                {
                    matched = false;
                    break;
                }

                ordered[i] = arguments[fieldIndex];
                assigned[fieldIndex] = true;
            }

            if (matched)
            {
                return constructor.Invoke(ordered);
            }
        }

        var instance = Activator.CreateInstance(type)
            ?? throw new NotSupportedException($"Server extension could not construct '{type}'.");
        for (var i = 0; i < fields.Count; i++)
        {
            fields[i].SetValue(instance, arguments[i]);
        }

        return instance;
    }

    private static int FieldIndex(IReadOnlyList<PropertyInfo> fields, string? name)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        var match = -1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
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

    private readonly record struct OptionalType(Type? Value);
}
