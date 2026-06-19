using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly ConcurrentDictionary<Type, OptionalType> ElementTypeCache = new();
    private static readonly ConcurrentDictionary<Type, OptionalMapTypes> MapTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Type> ListTypeCache = new();
    private static readonly ConcurrentDictionary<(Type Key, Type Value), Type> DictionaryTypeCache = new();
    private static readonly ConcurrentDictionary<Type, RecordShape> RecordShapeCache = new();
    private static readonly ConcurrentDictionary<Type, OptionalRecordShape> DtoShapeCache = new();

    private static Type? ElementType(Type type)
        => ElementTypeCache.GetOrAdd(type, static candidate => new OptionalType(FindElementType(candidate))).Value;

    private static (Type Key, Type Value)? MapTypes(Type type)
        => MapTypeCache.GetOrAdd(type, static candidate => new OptionalMapTypes(FindMapTypes(candidate))).Value;

    private static (Type Key, Type Value)? FindMapTypes(Type type)
    {
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition == typeof(Dictionary<,>) ||
                definition == typeof(IReadOnlyDictionary<,>) ||
                definition == typeof(IDictionary<,>))
            {
                var arguments = type.GetGenericArguments();
                return (arguments[0], arguments[1]);
            }
        }

        return null;
    }

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

    private static RecordShape? DtoShape(Type type)
        => DtoShapeCache.GetOrAdd(type, static candidate => new OptionalRecordShape(FindDtoShape(candidate))).Value;

    private static RecordShape? FindDtoShape(Type type)
    {
        if (type == typeof(string) ||
            type.IsPrimitive ||
            type.IsEnum ||
            ElementType(type) is not null ||
            MapTypes(type) is not null ||
            !(type.IsClass || type.IsValueType))
        {
            return null;
        }

        var shape = GetRecordShape(type);
        return shape.Fields.Count > 0 ? shape : null;
    }

    private static IList CreateList(Type elementType)
    {
        var listType = ListTypeCache.GetOrAdd(
            elementType,
            static type => typeof(List<>).MakeGenericType(type));
        return (IList)Activator.CreateInstance(listType)!;
    }

    private static IDictionary CreateDictionary(Type keyType, Type valueType)
    {
        var dictionaryType = DictionaryTypeCache.GetOrAdd(
            (keyType, valueType),
            static pair => typeof(Dictionary<,>).MakeGenericType(pair.Key, pair.Value));
        return (IDictionary)Activator.CreateInstance(dictionaryType)!;
    }

    private static RecordShape GetRecordShape(Type type)
        => RecordShapeCache.GetOrAdd(type, static candidate =>
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
            return new RecordShape(candidate, properties.ToArray());
        });

    private readonly record struct OptionalType(Type? Value);

    private readonly record struct OptionalMapTypes((Type Key, Type Value)? Value);

    private readonly record struct OptionalRecordShape(RecordShape? Value);
}
