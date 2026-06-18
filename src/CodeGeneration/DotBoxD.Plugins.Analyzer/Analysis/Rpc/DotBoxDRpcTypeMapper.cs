using Microsoft.CodeAnalysis;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

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
            case SpecialType.System_Boolean: return Scalar("Bool");
            case SpecialType.System_Int32: return Scalar("I32");
            case SpecialType.System_Int64: return Scalar("I64");
            case SpecialType.System_Double: return Scalar("F64");
            case SpecialType.System_String: return Scalar("String");
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return Scalar(EnumUsesI64(enumType) ? "I64" : "I32");
        }

        if (ListElementType(type) is { } elementType)
        {
            return $"{{\"name\":\"List\",\"arguments\":[{JsonType(elementType)}]}}";
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

    public static bool IsScalar(ITypeSymbol type)
        => type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Int32
            or SpecialType.System_Int64 or SpecialType.System_Double or SpecialType.System_String;

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

    public static bool IsRecordDto(INamedTypeSymbol type)
        => type.TypeKind is TypeKind.Class or TypeKind.Struct &&
           !IsScalar(type) &&
           !IsNullableValueType(type) &&
           RecordFields(type).Count > 0;

    /// <summary>The DTO's positional fields: public instance properties with a getter, in declaration
    /// order (for a positional record this is its primary-constructor parameter order).</summary>
    public static IReadOnlyList<IPropertySymbol> RecordFields(INamedTypeSymbol type)
    {
        var fields = new List<IPropertySymbol>();
        foreach (var member in type.GetMembers())
        {
            if (member is IPropertySymbol
                {
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false,
                    GetMethod: not null,
                    IsIndexer: false
                } property &&
                !property.IsImplicitlyDeclared)
            {
                fields.Add(property);
            }
        }

        return fields;
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
    private static void RejectInheritedDtoProperties(INamedTypeSymbol type)
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
                    !property.IsImplicitlyDeclared)
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
