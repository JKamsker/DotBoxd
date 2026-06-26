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

    private const BindingFlags DeclaredInstanceFlags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static RecordShape GetRecordShape(Type type)
        => RecordShapeCache.GetOrAdd(type, static candidate =>
        {
            var members = new List<RecordMember>();
            CollectReadableProperties(candidate, members);

            // A value type that carries its data in public fields rather than properties (e.g. a math vector
            // like System.Numerics.Vector3, whose X/Y/Z are float fields) has no readable properties; fall back
            // to its public instance fields so it still marshals as a record. The fallback only runs when there
            // are no properties, so property-based DTOs are unaffected and this stays strictly additive.
            if (members.Count == 0)
            {
                CollectPublicFields(candidate, members);
            }

            return new RecordShape(candidate, members.ToArray());
        });

    // The wire field set AND order must match the encoder exactly: the convention event adapter
    // (ConventionEventAdapter) and the analyzer (PluginEventPropertyReader) both walk the type hierarchy
    // base-first and, within each level, take public-getter instance properties in declaration (MetadataToken)
    // order. An inherited public property is therefore a real wire field — enumerating only the leaf type's
    // declared members would expect fewer fields than the encoder emits and throw on decode.
    private static void CollectReadableProperties(Type type, List<RecordMember> members)
    {
        foreach (var level in HierarchyBaseFirst(type))
        {
            var properties = level.GetProperties(DeclaredInstanceFlags);
            Array.Sort(properties, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (var property in properties)
            {
                // Require a public getter (not merely CanRead): a property with a public setter/init but a
                // non-public getter is excluded by the analyzer and the convention adapter, so the decode shape
                // must skip it too — otherwise the field count is larger than the encoder produced.
                if (property.GetMethod is { IsPublic: true } &&
                    property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                    !IsIgnoredMember(property))
                {
                    members.Add(RecordMember.FromProperty(property));
                }
            }
        }
    }

    private static void CollectPublicFields(Type type, List<RecordMember> members)
    {
        foreach (var level in HierarchyBaseFirst(type))
        {
            var fields = level.GetFields(DeclaredInstanceFlags);
            Array.Sort(fields, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (var field in fields)
            {
                if (!IsIgnoredMember(field))
                {
                    members.Add(RecordMember.FromField(field));
                }
            }
        }
    }

    // Base-first (most-derived last) so an inherited member precedes a declared one, matching the encoder order.
    private static List<Type> HierarchyBaseFirst(Type type)
    {
        var hierarchy = new List<Type>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            hierarchy.Add(current);
        }

        hierarchy.Reverse();
        return hierarchy;
    }

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
    private readonly record struct OptionalType(Type? Value);

    private readonly record struct OptionalMapTypes((Type Key, Type Value)? Value);

    private readonly record struct OptionalRecordShape(RecordShape? Value);
}
