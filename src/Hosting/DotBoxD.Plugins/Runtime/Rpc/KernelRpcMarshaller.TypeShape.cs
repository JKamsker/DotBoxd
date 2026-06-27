using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

using LinqExpression = System.Linq.Expressions.Expression;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static readonly ConcurrentDictionary<Type, OptionalType> ElementTypeCache = new();
    private static readonly ConcurrentDictionary<Type, OptionalMapTypes> MapTypeCache = new();
    private static readonly ConcurrentDictionary<Type, Func<int, IList>> ListFactoryCache = new();
    private static readonly ConcurrentDictionary<(Type Key, Type Value), Func<int, IDictionary>> DictionaryFactoryCache = new();
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

    private static IList CreateList(Type elementType, int capacity)
        => ListFactoryCache.GetOrAdd(elementType, CreateListFactory)(capacity);

    private static IDictionary CreateDictionary(Type keyType, Type valueType, int capacity)
        => DictionaryFactoryCache.GetOrAdd((keyType, valueType), CreateDictionaryFactory)(capacity);

    private static Func<int, IList> CreateListFactory(Type elementType)
    {
        var constructor = typeof(List<>)
            .MakeGenericType(elementType)
            .GetConstructor([typeof(int)])
            ?? throw new MissingMethodException($"List<{elementType}>", ".ctor(int)");
        return CompileCollectionFactory<IList>(constructor);
    }

    private static Func<int, IDictionary> CreateDictionaryFactory((Type Key, Type Value) types)
    {
        var constructor = typeof(Dictionary<,>)
            .MakeGenericType(types.Key, types.Value)
            .GetConstructor([typeof(int)])
            ?? throw new MissingMethodException($"Dictionary<{types.Key},{types.Value}>", ".ctor(int)");
        return CompileCollectionFactory<IDictionary>(constructor);
    }

    private static Func<int, TCollection> CompileCollectionFactory<TCollection>(ConstructorInfo constructor)
    {
        var capacity = LinqExpression.Parameter(typeof(int), "capacity");
        var created = LinqExpression.New(constructor, capacity);
        return LinqExpression.Lambda<Func<int, TCollection>>(
            LinqExpression.Convert(created, typeof(TCollection)),
            capacity).Compile();
    }

    private static RecordShape GetRecordShape(Type type)
        => RecordShapeCache.GetOrAdd(type, static candidate =>
        {
            var members = new List<RecordMember>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var property in candidate.GetProperties(flags))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                    !IsIgnoredMember(property))
                {
                    members.Add(RecordMember.FromProperty(property));
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
                        members.Add(RecordMember.FromField(field));
                    }
                }
            }

            members.Sort(static (left, right) => left.Member.MetadataToken.CompareTo(right.Member.MetadataToken));
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
    private readonly record struct OptionalType(Type? Value);

    private readonly record struct OptionalMapTypes((Type Key, Type Value)? Value);

    private readonly record struct OptionalRecordShape(RecordShape? Value);
}
