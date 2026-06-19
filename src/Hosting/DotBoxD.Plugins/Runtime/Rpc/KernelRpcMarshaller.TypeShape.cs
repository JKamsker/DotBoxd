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

    private static bool IsDto(Type type)
        => type != typeof(string) &&
           !type.IsPrimitive &&
           !type.IsEnum &&
           ElementType(type) is null &&
           MapTypes(type) is null &&
           (type.IsClass || type.IsValueType) &&
           GetRecordShape(type).Fields.Count > 0;

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

    private sealed class RecordShape
    {
        private readonly ConstructorInfo? _constructor;
        private readonly int[] _constructorMap;
        private readonly bool _constructorUsesFieldOrder;
        private readonly Type _type;

        public RecordShape(Type type, PropertyInfo[] fields)
        {
            _type = type;
            Fields = fields;
            (_constructor, _constructorMap) = FindConstructor(type, fields);
            _constructorUsesFieldOrder = IsIdentityMap(_constructorMap);
        }

        public IReadOnlyList<PropertyInfo> Fields { get; }

        public object Construct(object?[] arguments)
        {
            if (_constructor is not null)
            {
                return _constructor.Invoke(_constructorUsesFieldOrder ? arguments : OrderArguments(arguments));
            }

            var instance = Activator.CreateInstance(_type)
                ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
            for (var i = 0; i < Fields.Count; i++)
            {
                Fields[i].SetValue(instance, arguments[i]);
            }

            return instance;
        }

        private object?[] OrderArguments(object?[] arguments)
        {
            var ordered = new object?[_constructorMap.Length];
            for (var i = 0; i < ordered.Length; i++)
            {
                ordered[i] = arguments[_constructorMap[i]];
            }

            return ordered;
        }

        private static (ConstructorInfo? Constructor, int[] Map) FindConstructor(
            Type type,
            IReadOnlyList<PropertyInfo> fields)
        {
            foreach (var constructor in type.GetConstructors())
            {
                var parameters = constructor.GetParameters();
                if (parameters.Length != fields.Count || parameters.Length == 0)
                {
                    continue;
                }

                var map = new int[parameters.Length];
                var assigned = new bool[parameters.Length];
                if (TryMapConstructor(parameters, fields, map, assigned))
                {
                    return (constructor, map);
                }
            }

            return (null, []);
        }

        private static bool TryMapConstructor(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<PropertyInfo> fields,
            int[] map,
            bool[] assigned)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                var fieldIndex = FieldIndex(fields, parameters[i].Name);
                if (fieldIndex < 0 ||
                    assigned[fieldIndex] ||
                    parameters[i].ParameterType != fields[fieldIndex].PropertyType)
                {
                    return false;
                }

                map[i] = fieldIndex;
                assigned[fieldIndex] = true;
            }

            return true;
        }

        private static bool IsIdentityMap(IReadOnlyList<int> map)
        {
            for (var i = 0; i < map.Count; i++)
            {
                if (map[i] != i)
                {
                    return false;
                }
            }

            return true;
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
    }

    private readonly record struct OptionalType(Type? Value);

    private readonly record struct OptionalMapTypes((Type Key, Type Value)? Value);
}
