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
        ThrowIfUnsupportedFrameworkStruct(type);
        if (IsDateTimeWireType(type) ||
            type == typeof(TimeSpan) ||
            type == typeof(string) ||
            type.IsPrimitive ||
            type.IsEnum ||
            ElementType(type) is not null ||
            MapTypes(type) is not null ||
            !(type.IsClass || type.IsValueType) ||
            ImplementsGenericEnumerable(type))
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

    private static void ThrowIfUnsupportedFrameworkStruct(Type type)
    {
        if (type == typeof(DateOnly) ||
            type == typeof(TimeOnly) ||
            type == typeof(Index) ||
            type == typeof(Range) ||
            type == typeof(CancellationToken))
        {
            throw new NotSupportedException(
                $"Kernel RPC service type '{type}' is not supported; convert it to a supported scalar or DTO type.");
        }
    }

    // An IEnumerable<T> reaches here only after the recognized list/map shapes have been ruled out, so any
    // remaining one — e.g. ImmutableArray<T>, ImmutableList<T>, Queue<T> — exposes only scalar getters
    // (Length/Count/...) and would otherwise be mis-marshalled as a metadata-only record that silently drops its
    // elements. Excluding it makes the type fail closed with the marshaller's unsupported-type exception, mirroring
    // the analyzer's DotBoxDRpcTypeMapper.ImplementsGenericEnumerable. A plain DTO does not implement
    // IEnumerable<T>, so this does not over-exclude.
    private static bool ImplementsGenericEnumerable(Type type)
    {
        foreach (var @interface in type.GetInterfaces())
        {
            if (@interface.IsGenericType &&
                @interface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return true;
            }
        }

        return false;
    }

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
            RejectInheritedDtoMembers(candidate);

            var members = new List<RecordMember>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var properties = candidate.GetProperties(flags);
            Array.Sort(properties, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (var property in properties)
            {
                if (property.GetMethod is { IsPublic: true } &&
                    property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                    !IsIgnoredMember(property))
                {
                    members.Add(RecordMember.FromProperty(property));
                }
            }

            // Reflection reports declared properties and fields separately, so the wire shape is readable
            // properties first, then public fields. That keeps existing property-only DTOs stable while mixed
            // DTOs still marshal every public data member.
            var fields = candidate.GetFields(flags);
            Array.Sort(fields, static (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
            foreach (var field in fields)
            {
                if (!IsIgnoredMember(field))
                {
                    members.Add(RecordMember.FromField(field));
                }
            }

            return new RecordShape(candidate, members.ToArray());
        });

    private static void RejectInheritedDtoMembers(Type type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType == typeof(object) || baseType == typeof(ValueType))
            {
                continue;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var property in baseType.GetProperties(flags))
            {
                if (property.GetMethod is not null &&
                    property.GetIndexParameters().Length == 0 &&
                    !string.Equals(property.Name, "EqualityContract", StringComparison.Ordinal) &&
                    !IsIgnoredMember(property))
                {
                    throw new NotSupportedException(
                        $"Server extension DTO '{type}' inherits public properties from base type " +
                        $"'{baseType}'; flatten the DTO into a single type.");
                }
            }

            foreach (var field in baseType.GetFields(flags))
            {
                if (!field.IsLiteral && !IsIgnoredMember(field))
                {
                    throw new NotSupportedException(
                        $"Server extension DTO '{type}' inherits public fields from base type " +
                        $"'{baseType}'; flatten the DTO into a single type.");
                }
            }
        }
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
