using Microsoft.CodeAnalysis;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;
namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;
/// <summary>
/// Maps C# types used by a <c>[ServerExtension]</c> batch method onto DotBoxD.Kernels JSON IR types: scalars to
/// their sandbox names, <c>List&lt;T&gt;</c>/<c>IEnumerable&lt;T&gt;</c>/<c>T[]</c> to <c>List</c>, and a
/// DTO (record/struct/class of supported fields) to a positional <c>Record</c>. A DTO's fields are its
/// public readable properties followed by public instance fields, which is also the order <c>record.new</c>
/// arguments and <c>record.get</c> indices use. Anything unsupported throws <see cref="NotSupportedException"/> so
/// the whole kernel fails generation safely.
/// </summary>
internal static partial class DotBoxDRpcTypeMapper
{
    private const int MaxJsonTypeDepth = 8;

    public static string JsonType(ITypeSymbol type)
        => JsonType(type, 0, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));

    private static string JsonType(ITypeSymbol type, int depth, HashSet<ITypeSymbol> visiting)
    {
        if (type.SpecialType == SpecialType.System_Void || DotBoxDRpcReturnType.IsTaskLike(type))
        {
            throw new NotSupportedException(
                $"Server extension type '{type.ToDisplayString()}' is only supported as a top-level return type.");
        }
        if (DotBoxDNullableScalarType.IsNullableValueType(type))
        {
            throw new NotSupportedException($"Server extension nullable type '{type.ToDisplayString()}' is not supported.");
        }
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.IsReferenceType)
        {
            throw new NotSupportedException(
                $"Server extension nullable reference type '{type.ToDisplayString()}' is not supported; " +
                "kernel RPC does not encode null reference values.");
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
            RejectTooDeep(type, depth);
            return $"{{\"name\":\"List\",\"arguments\":[{JsonType(elementType, depth + 1, visiting)}]}}";
        }
        if (MapTypes(type) is { } map)
        {
            RejectTooDeep(type, depth);
            if (!IsSupportedMapKey(map.Key))
            {
                throw new NotSupportedException(
                    $"Server extension map key type '{map.Key.ToDisplayString()}' is not supported; " +
                    "map keys must be bool, int, long, string, or an enum.");
            }
            return $"{{\"name\":\"Map\",\"arguments\":[{JsonType(map.Key, depth + 1, visiting)},{JsonType(map.Value, depth + 1, visiting)}]}}";
        }
        if (type is INamedTypeSymbol named && IsRecordDto(named))
        {
            RejectTooDeep(type, depth);
            if (!visiting.Add(named))
            {
                throw new NotSupportedException(
                    $"Server extension DTO type '{named.ToDisplayString()}' is cyclic; recursive DTO shapes are not supported.");
            }

            RejectInheritedDtoProperties(named);
            try
            {
                var fields = RecordFields(named);
                var fieldTypes = new List<string>(fields.Count);
                foreach (var field in fields)
                {
                    fieldTypes.Add(JsonType(field.Type, depth + 1, visiting));
                }
                return $"{{\"name\":\"Record\",\"arguments\":[{string.Join(",", fieldTypes)}]}}";
            }
            finally
            {
                visiting.Remove(named);
            }
        }
        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

    private static void RejectTooDeep(ITypeSymbol type, int depth)
    {
        if (depth >= MaxJsonTypeDepth)
        {
            throw new NotSupportedException(
                $"Server extension type '{type.ToDisplayString()}' exceeds the supported RPC shape depth.");
        }
    }
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
           !IsUnsupportedFrameworkStruct(type) &&
           !DotBoxDNullableScalarType.IsNullableValueType(type) &&
           ListElementType(type) is null &&
           MapTypes(type) is null &&
           RecordFields(type).Count > 0;

    /// <summary>
    /// The DTO's positional fields: public readable properties first, then public instance fields. That order
    /// mirrors the runtime reflection shape, keeps existing property DTOs stable, and lets a DTO that mixes
    /// properties with fields marshal every public wire member instead of silently dropping fields.
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
                property.GetMethod.DeclaredAccessibility == Accessibility.Public &&
                !property.IsImplicitlyDeclared &&
                !IsIgnoredDataMember(property))
            {
                members.Add(new RecordMember(property.Name, property.Type, property));
            }
        }

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

    private static string Scalar(string name) => "\"" + name + "\"";

    private static bool IsUnsupportedFrameworkStruct(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace.ToDisplayString();
        return (ns == "System" &&
                type.Name is "DateTime" or "DateTimeOffset" or "TimeSpan"
                    or "DateOnly" or "TimeOnly" or "Index" or "Range") ||
               (ns == "System.Threading" &&
                type.Name == "CancellationToken");
    }
}
