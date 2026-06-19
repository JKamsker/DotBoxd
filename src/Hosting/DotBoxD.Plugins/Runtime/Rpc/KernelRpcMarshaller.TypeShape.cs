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
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var members = new List<MemberInfo>();
            foreach (var property in candidate.GetProperties(flags))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                    !IsIgnoredMember(property))
                {
                    members.Add(property);
                }
            }

            // A value type that carries its data in public fields rather than properties (e.g. a math vector
            // like System.Numerics.Vector3, whose X/Y/Z are float fields) has no readable properties; fall back
            // to its public instance fields so it still marshals as a record. The fallback only runs when there
            // are no properties, so property-based DTOs are unaffected and this stays strictly additive.
            if (members.Count == 0)
            {
                foreach (var field in candidate.GetFields(flags))
                {
                    if (!IsIgnoredMember(field))
                    {
                        members.Add(field);
                    }
                }
            }

            members.Sort(static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            return new RecordShape(candidate, members.ToArray());
        });

    // A member marked [IgnoreDataMember] (System.Runtime.Serialization) is non-wire — a lazily-resolved or
    // computed member, not serialized data — so it is excluded from the marshalled record shape, matching the
    // analyzer (DotBoxDRpcTypeMapper.IsIgnoredDataMember) and the convention event adapter so all three readers
    // agree on the wire field set. Matched by name via GetCustomAttributesData so the attribute need not load.
    internal static bool IsIgnoredMember(MemberInfo member)
    {
        foreach (var attribute in member.GetCustomAttributesData())
        {
            if (string.Equals(
                    attribute.AttributeType.FullName,
                    "System.Runtime.Serialization.IgnoreDataMemberAttribute",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // A record member is a public property (with a getter) or, for a field-only value type, a public field.
    // These helpers read either uniformly so the marshaller treats both the same.
    private static Type RecordMemberType(MemberInfo member)
        => member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

    private static object? GetRecordMemberValue(MemberInfo member, object instance)
        => member is PropertyInfo property ? property.GetValue(instance) : ((FieldInfo)member).GetValue(instance);

    private static void SetRecordMemberValue(MemberInfo member, object instance, object? value)
    {
        if (member is PropertyInfo property)
        {
            property.SetValue(instance, value);
        }
        else
        {
            ((FieldInfo)member).SetValue(instance, value);
        }
    }

    private sealed class RecordShape
    {
        private readonly ConstructorInfo? _constructor;
        private readonly int[] _constructorMap;
        private readonly bool _constructorUsesFieldOrder;
        private readonly Type _type;

        public RecordShape(Type type, MemberInfo[] fields)
        {
            _type = type;
            Fields = fields;
            (_constructor, _constructorMap) = FindConstructor(type, fields);
            _constructorUsesFieldOrder = IsIdentityMap(_constructorMap);
        }

        public IReadOnlyList<MemberInfo> Fields { get; }

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
                SetRecordMemberValue(Fields[i], instance, arguments[i]);
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
            IReadOnlyList<MemberInfo> fields)
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
            IReadOnlyList<MemberInfo> fields,
            int[] map,
            bool[] assigned)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                var fieldIndex = FieldIndex(fields, parameters[i].Name);
                if (fieldIndex < 0 ||
                    assigned[fieldIndex] ||
                    parameters[i].ParameterType != RecordMemberType(fields[fieldIndex]))
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

        private static int FieldIndex(IReadOnlyList<MemberInfo> fields, string? name)
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
