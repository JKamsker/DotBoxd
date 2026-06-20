using DotBoxD.Plugins.Analyzer.Analysis.Lowering;
using Microsoft.CodeAnalysis;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Maps C# types used by a <c>[ServerExtension]</c> batch method onto DotBoxD.Kernels JSON IR types: scalars to
/// their sandbox names, <c>List&lt;T&gt;</c>/<c>IEnumerable&lt;T&gt;</c>/<c>T[]</c> to <c>List</c>, and a
/// DTO (record/struct/class of supported fields) to a positional <c>Record</c>. A DTO's fields are its
/// public instance properties in declaration order, which is also the order <c>record.new</c> arguments
/// and <c>record.get</c> indices use. Anything unsupported throws <see cref="NotSupportedException"/> so
/// the whole kernel fails generation safely.
/// </summary>
internal static class DotBoxDRpcTypeMapper
{
    public static string JsonType(ITypeSymbol type)
    {
        type = DotBoxDTypeNameReader.UnwrapTaskLike(type);
        if (IsNullableValueType(type))
        {
            throw new NotSupportedException($"Server extension nullable type '{type.ToDisplayString()}' is not supported.");
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return Scalar("Bool");
            case SpecialType.System_Int32:
                return Scalar("I32");
            case SpecialType.System_Int64:
                return Scalar("I64");
            case SpecialType.System_Double:
                return Scalar("F64");
            case SpecialType.System_Single:
                return Scalar("F64");
            case SpecialType.System_String:
                return Scalar("String");
        }

        if (IsGuid(type))
        {
            return Scalar("Guid");
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return Scalar(EnumUsesI64(enumType) ? "I64" : "I32");
        }

        if (ListElementType(type) is { } elementType)
        {
            return $"{{\"name\":\"List\",\"arguments\":[{JsonType(elementType)}]}}";
        }

        if (MapTypes(type) is { } map)
        {
            if (!IsSupportedMapKey(map.Key))
            {
                throw new NotSupportedException(
                    $"Server extension map key type '{map.Key.ToDisplayString()}' is not supported; " +
                    "map keys must be bool, int, long, string, or an enum.");
            }

            return $"{{\"name\":\"Map\",\"arguments\":[{JsonType(map.Key)},{JsonType(map.Value)}]}}";
        }

        if (type is INamedTypeSymbol named && IsRecordDto(named))
        {
            RejectInheritedDtoProperties(named);
            var fields = RecordFields(named);
            var fieldTypes = new List<string>(fields.Count);
            foreach (var field in fields)
            {
                fieldTypes.Add(JsonType(field.Type));
            }

            return $"{{\"name\":\"Record\",\"arguments\":[{string.Join(",", fieldTypes)}]}}";
        }

        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

    /// <summary>
    /// True when <paramref name="member"/> can be written through an object initializer: a property with an
    /// accessible <c>set</c>/<c>init</c>, or a non-readonly public field. Used for the parameterless-construct
    /// + object-initializer fallback when a DTO exposes no constructor matching its fields.
    /// </summary>
    public static bool IsObjectInitializerWritable(RecordMember member)
        => member.Symbol switch
        {
            IPropertySymbol
            {
                SetMethod.DeclaredAccessibility:
                    Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal
            } => true,
            IFieldSymbol { IsReadOnly: false, IsConst: false } => true,
            _ => false
        };

    public static bool IsScalar(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32
            or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_Single
            or SpecialType.System_String;

    /// <summary><see cref="System.Guid"/> is a first-class 16-byte scalar (sandbox <c>Guid</c> kind), distinct
    /// from <c>string</c>. Detected structurally so it is robust to display-format differences.</summary>
    public static bool IsGuid(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "Guid", ContainingNamespace: { Name: "System" } ns }
           && ns.ContainingNamespace is { IsGlobalNamespace: true };

    /// <summary>The element type of a list-shaped parameter/return (<c>List&lt;T&gt;</c>,
    /// <c>IReadOnlyList&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, or <c>T[]</c>), else null.</summary>
    public static ITypeSymbol? ListElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            if (array.Rank != 1)
            {
                throw new NotSupportedException(
                    $"Server extension multidimensional array type '{array.ToDisplayString()}' is not supported.");
            }

            return array.ElementType;
        }

        if (type is INamedTypeSymbol { IsGenericType: true } named)
        {
            var definition = named.ConstructedFrom.ToDisplayString();
            if (definition is TypeNames.ListOriginal
                or TypeNames.ReadOnlyListOriginal
                or TypeNames.ListInterfaceOriginal
                or TypeNames.EnumerableOriginal
                or TypeNames.ReadOnlyCollectionOriginal)
            {
                return named.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>The key and value types of a map-shaped parameter/return/field (<c>Dictionary&lt;K,V&gt;</c>,
    /// <c>IReadOnlyDictionary&lt;K,V&gt;</c>, or <c>IDictionary&lt;K,V&gt;</c>), else null.</summary>
    public static (ITypeSymbol Key, ITypeSymbol Value)? MapTypes(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true } named && named.TypeArguments.Length == 2)
        {
            var definition = named.ConstructedFrom.ToDisplayString();
            if (definition is TypeNames.DictionaryOriginal
                or TypeNames.ReadOnlyDictionaryOriginal
                or TypeNames.DictionaryInterfaceOriginal)
            {
                return (named.TypeArguments[0], named.TypeArguments[1]);
            }
        }

        return null;
    }

    /// <summary>A map key must lower to a scalar the kernel verifier accepts as a key: <c>bool</c>,
    /// <c>int</c>, <c>long</c>, <c>string</c>, or an enum (which lowers to <c>I32</c>/<c>I64</c>).
    /// <c>double</c> and composite types are rejected.</summary>
    public static bool IsSupportedMapKey(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32
               or SpecialType.System_Int64 or SpecialType.System_String
           || type.TypeKind == TypeKind.Enum;

    public static bool SupportsIndexedListWrite(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true } named)
        {
            return false;
        }

        var definition = named.ConstructedFrom.ToDisplayString();
        return definition is TypeNames.ListOriginal
            or TypeNames.ReadOnlyListOriginal
            or TypeNames.ListInterfaceOriginal;
    }

    public static bool IsRecordDto(INamedTypeSymbol type)
        => type.TypeKind is TypeKind.Class or TypeKind.Struct &&
           !IsScalar(type) &&
           !IsNullableValueType(type) &&
           MapTypes(type) is null &&
           RecordFields(type).Count > 0;

    /// <summary>
    /// The DTO's positional fields, in declaration order (for a positional record this is its primary-constructor
    /// parameter order): its public instance properties with a getter, or — for a value type that carries its
    /// data in public fields rather than properties (e.g. <c>System.Numerics.Vector3</c>, whose <c>X/Y/Z</c> are
    /// <c>float</c> fields) — its public instance fields. The field fallback only runs when there are no readable
    /// properties, so a property-based DTO is unaffected and the change is strictly additive.
    /// </summary>
    public static IReadOnlyList<RecordMember> RecordFields(INamedTypeSymbol type)
    {
        var members = new List<RecordMember>();
        foreach (var member in type.GetMembers())
        {
            if (member is IPropertySymbol
                {
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false,
                    GetMethod: not null,
                    IsIndexer: false
                } property &&
                !property.IsImplicitlyDeclared &&
                !IsIgnoredDataMember(property))
            {
                members.Add(new RecordMember(property.Name, property.Type, property));
            }
        }

        if (members.Count == 0)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IFieldSymbol
                    {
                        DeclaredAccessibility: Accessibility.Public,
                        IsStatic: false,
                        IsConst: false
                    } field &&
                    !field.IsImplicitlyDeclared &&
                    !IsIgnoredDataMember(field))
                {
                    members.Add(new RecordMember(field.Name, field.Type, field));
                }
            }
        }

        return members;
    }

    /// <summary>
    /// True when <paramref name="member"/> is marked <c>[IgnoreDataMember]</c>
    /// (<c>System.Runtime.Serialization</c>). Such a member is non-wire — a lazily-resolved or computed
    /// projection of the record (e.g. a context snapshot resolved on first read), not part of its serialized
    /// data — so it is excluded from the marshalled record/event field set. The runtime convention adapter and
    /// the decode-side record shape exclude it too, keeping the analyzer's wire field set in lockstep with both
    /// runtime readers, and letting a record/event that carries such a member still lower.
    /// </summary>
    public static bool IsIgnoredDataMember(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass is { } attributeClass &&
                string.Equals(
                    attributeClass.ToDisplayString(),
                    "System.Runtime.Serialization.IgnoreDataMemberAttribute",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>An enum marshals through its underlying integer; widths that overflow <c>I32</c>
    /// (<c>uint</c>, <c>long</c>, <c>ulong</c>) use <c>I64</c>, everything else <c>I32</c>.</summary>
    public static bool EnumUsesI64(INamedTypeSymbol enumType)
        => enumType.EnumUnderlyingType?.SpecialType is
            SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64;

    /// <summary>
    /// Rejects a DTO that inherits public instance properties from a base type: <see cref="RecordFields"/>
    /// only sees declared members (so inherited fields would be silently dropped on both the analyzer and the
    /// runtime marshaller). Fail generation with a clear message instead.
    /// </summary>
    internal static void RejectInheritedDtoProperties(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType)
            {
                continue;
            }

            foreach (var member in baseType.GetMembers())
            {
                if (member is IPropertySymbol
                    {
                        DeclaredAccessibility: Accessibility.Public,
                        IsStatic: false,
                        GetMethod: not null,
                        IsIndexer: false
                    } property &&
                    !property.IsImplicitlyDeclared &&
                    !IsIgnoredDataMember(property))
                {
                    throw new NotSupportedException(
                        $"Server extension DTO '{type.ToDisplayString()}' must not inherit public properties from " +
                        $"base type '{baseType.ToDisplayString()}'; flatten the DTO into a single type.");
                }
            }
        }
    }

    private static string Scalar(string name) => "\"" + name + "\"";

    private static bool IsNullableValueType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T;
}

/// <summary>
/// A DTO/record field for marshalling: a public property, or (for a field-only value type) a public field.
/// <see cref="Symbol"/> is the underlying property/field symbol for callers that need more than name and type.
/// </summary>
internal readonly record struct RecordMember(string Name, ITypeSymbol Type, ISymbol Symbol);
