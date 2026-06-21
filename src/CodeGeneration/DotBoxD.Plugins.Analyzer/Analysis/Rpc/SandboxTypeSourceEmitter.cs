using Microsoft.CodeAnalysis;
using ManifestTypes = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.ManifestTypes;
using TypeNames = DotBoxD.Plugins.Analyzer.Analysis.Lowering.DotBoxDGenerationNames.TypeNames;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

/// <summary>
/// Emits the C# expression that constructs the <c>SandboxType</c> for a marshaller-eligible CLR type, mirroring
/// the runtime <c>KernelRpcMarshaller.SandboxTypeOf</c> so a lowered remote chain's kernel parameter and
/// projection return types line up exactly with the values the runtime convention adapter produces:
/// scalars and <see cref="System.Guid"/> to their sandbox scalar, enums to <c>I32</c>/<c>I64</c> by underlying
/// width, <c>List&lt;T&gt;</c>/<c>T[]</c> to <c>List</c>, <c>Dictionary&lt;K,V&gt;</c> to <c>Map</c>, and a DTO
/// record to a positional <c>Record</c>. Anything outside that set is not wire-eligible.
/// </summary>
internal static class SandboxTypeSourceEmitter
{
    private const string SandboxType = TypeNames.GlobalSandboxType;

    // A record field whose type leads back to an enclosing record (directly or through a list/map/record) would
    // recurse forever, so the depth of a record/list/map nesting chain is bounded. The bound is kept at or below
    // the kernel verifier's structural depth limit (SandboxType.IsKnown defaults to maxDepth 8) so a type the
    // analyzer emits is never rejected at install as "unknown"; anything deeper is treated as not
    // marshaller-eligible and the chain fails safe at generation.
    private const int MaxDepth = 8;

    /// <summary>The <c>SandboxType</c> construction source for <paramref name="type"/>, or <c>null</c> when it
    /// is not marshaller-eligible (so the caller fails the chain safe rather than emitting an invalid kernel).</summary>
    public static string? TryEmit(ITypeSymbol type)
    {
        try
        {
            return Emit(type, 0);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The coarse manifest tag the expression lowerer carries for an event property of
    /// <paramref name="type"/>: a scalar token for scalars (enums reuse their underlying integer token), a
    /// non-scalar shape tag for Guid/list/map/record, or <see cref="ManifestTypes.Unsupported"/> when the type
    /// cannot be marshalled. Tag eligibility is kept in lockstep with <see cref="TryEmit"/>.</summary>
    public static string ManifestTag(ITypeSymbol type)
    {
        if (TryEmit(type) is null)
        {
            return ManifestTypes.Unsupported;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return ManifestTypes.Bool;
            case SpecialType.System_Int32:
                return ManifestTypes.Int;
            case SpecialType.System_Int64:
                return ManifestTypes.Long;
            case SpecialType.System_Double:
                return ManifestTypes.Double;
            case SpecialType.System_Single:
                return ManifestTypes.Double;
            case SpecialType.System_String:
                return ManifestTypes.String;
        }

        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return ManifestTypes.Guid;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType) ? ManifestTypes.Long : ManifestTypes.Int;
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
        {
            return ManifestTypes.List;
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is not null)
        {
            return ManifestTypes.Map;
        }

        return ManifestTypes.Record;
    }

    private static string Emit(ITypeSymbol type, int depth)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return SandboxType + ".Bool";
            case SpecialType.System_Int32:
                return SandboxType + ".I32";
            case SpecialType.System_Int64:
                return SandboxType + ".I64";
            case SpecialType.System_Double:
                return SandboxType + ".F64";
            case SpecialType.System_Single:
                return SandboxType + ".F64";
            case SpecialType.System_String:
                return SandboxType + ".String";
        }

        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return SandboxType + ".Guid";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType) ? SandboxType + ".I64" : SandboxType + ".I32";
        }

        // Past this point the type nests (list/map/record); a self-referential DTO would otherwise recurse
        // without bound. Reject once the nesting chain grows past the limit — the caller fails the chain safe.
        if (depth >= MaxDepth)
        {
            throw new NotSupportedException();
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is { } elementType)
        {
            return $"{SandboxType}.List({Emit(elementType, depth + 1)})";
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            if (!DotBoxDRpcTypeMapper.IsSupportedMapKey(map.Key))
            {
                throw new NotSupportedException();
            }

            return $"{SandboxType}.Map({Emit(map.Key, depth + 1)}, {Emit(map.Value, depth + 1)})";
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            // A DTO that inherits public instance properties would silently drop them: RecordFields (and the
            // runtime marshaller's GetRecordShape) see only declared members. Fail safe instead of emitting a
            // partial record shape — same rule the server-extension JsonType path enforces.
            DotBoxDRpcTypeMapper.RejectInheritedDtoProperties(named);
            var fields = DotBoxDRpcTypeMapper.RecordFields(named);
            var fieldTypes = new string[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                fieldTypes[i] = Emit(fields[i].Type, depth + 1);
            }

            return $"{SandboxType}.Record(new {SandboxType}[] {{ {string.Join(", ", fieldTypes)} }})";
        }

        throw new NotSupportedException();
    }
}
