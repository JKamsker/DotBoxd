namespace SafeIR.Verifier;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class MetadataTableVerifier
{
    private static readonly (TableIndex Table, string Code, string Message)[] ForbiddenTables = [
        (TableIndex.InterfaceImpl, "V-METADATA-SHAPE", "interface implementations are not allowed"),
        (TableIndex.DeclSecurity, "V-SECURITY", "declarative security metadata is not allowed"),
        (TableIndex.FieldMarshal, "V-PINVOKE", "field marshalling metadata is not allowed"),
        (TableIndex.ClassLayout, "V-METADATA-SHAPE", "explicit class layout metadata is not allowed"),
        (TableIndex.FieldLayout, "V-METADATA-SHAPE", "field layout metadata is not allowed"),
        (TableIndex.EventMap, "V-METADATA-SHAPE", "event metadata is not allowed"),
        (TableIndex.Event, "V-METADATA-SHAPE", "event metadata is not allowed"),
        (TableIndex.PropertyMap, "V-METADATA-SHAPE", "property metadata is not allowed"),
        (TableIndex.Property, "V-METADATA-SHAPE", "property metadata is not allowed"),
        (TableIndex.MethodSemantics, "V-METADATA-SHAPE", "property or event method semantics are not allowed"),
        (TableIndex.MethodImpl, "V-METADATA-SHAPE", "method implementation metadata is not allowed"),
        (TableIndex.ModuleRef, "V-PINVOKE", "module references are not allowed"),
        (TableIndex.FieldRva, "V-METADATA-SHAPE", "field RVA metadata is not allowed"),
        (TableIndex.ExportedType, "V-PUBLIC-SURFACE", "exported type metadata is not allowed"),
        (TableIndex.File, "V-RESOURCE", "file metadata is not allowed"),
        (TableIndex.NestedClass, "V-METADATA-SHAPE", "nested types are not allowed"),
        (TableIndex.MethodSpec, "V-GENERIC", "generic method specifications are not allowed"),
        (TableIndex.GenericParamConstraint, "V-GENERIC", "generic parameter constraints are not allowed")
    ];

    public static void Verify(MetadataReader reader, List<VerificationDiagnostic> diagnostics)
    {
        foreach (var item in ForbiddenTables) {
            if (reader.GetTableRowCount(item.Table) == 0) {
                continue;
            }

            diagnostics.Add(new VerificationDiagnostic(item.Code, item.Message));
        }
    }
}
