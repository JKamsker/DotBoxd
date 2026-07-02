namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private List<int> InitializerFieldIndexes(
        IReadOnlyList<RecordMember> fields,
        bool[]? assigned)
    {
        var initialized = new List<int>();
        for (var i = 0; i < fields.Count; i++)
        {
            if (assigned is not null &&
                !DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i], _compilation))
            {
                continue;
            }

            initialized.Add(i);
        }

        return initialized;
    }

    private void AppendReadOnlyFieldVerifications(
        StringBuilder builder,
        IReadOnlyList<RecordMember> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (DotBoxDRpcTypeMapper.IsObjectInitializerWritable(fields[i], _compilation))
            {
                continue;
            }

            if (!DotBoxDRpcTypeMapper.IsReadableFromGeneratedCode(fields[i], _compilation))
            {
                throw new NotSupportedException(
                    $"Server extension DTO field '{fields[i].Name}' is private or read-only and could not be reconstructed.");
            }

            builder.Append("        if (!global::System.Collections.Generic.EqualityComparer<")
                .Append(TypeName(fields[i].Type)).Append(">.Default.Equals(__result.")
                .Append(Identifier(fields[i].Name)).Append(", ")
                .Append(FieldLocal(i)).AppendLine("))");
            builder.AppendLine("        {");
            builder.Append("            throw new global::System.NotSupportedException(\"Server extension DTO field '")
                .Append(fields[i].Name)
                .AppendLine("' is private or read-only and could not be reconstructed.\");");
            builder.AppendLine("        }");
        }
    }

    private static string FieldLocal(int index)
        => "__field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
