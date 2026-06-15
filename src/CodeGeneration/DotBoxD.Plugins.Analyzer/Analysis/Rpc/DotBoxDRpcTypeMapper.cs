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
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return Scalar("Bool");
            case SpecialType.System_Int32: return Scalar("I32");
            case SpecialType.System_Int64: return Scalar("I64");
            case SpecialType.System_Double: return Scalar("F64");
            case SpecialType.System_String: return Scalar("String");
        }

        if (ListElementType(type) is { } elementType)
        {
            return $"{{\"name\":\"List\",\"arguments\":[{JsonType(elementType)}]}}";
        }

        if (type is INamedTypeSymbol named && IsRecordDto(named))
        {
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

    private static string Scalar(string name) => "\"" + name + "\"";
}
