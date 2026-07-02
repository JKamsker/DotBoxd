namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class RpcKernelPayloadReadEmitter
{
    private string EnsureDecimalPayloadReader()
    {
        const string key = "decimal-reader:System.Decimal";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("    private static decimal ").Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __fieldCount = reader.ReadRecordHeader();");
        _helpers.AppendLine("        if (__fieldCount != 4)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension Decimal wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        try");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            return new decimal(new int[]");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                reader.ReadInt32(),");
        _helpers.AppendLine("                reader.ReadInt32(),");
        _helpers.AppendLine("                reader.ReadInt32(),");
        _helpers.AppendLine("                reader.ReadInt32(),");
        _helpers.AppendLine("            });");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("        catch (global::System.ArgumentException ex)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension Decimal wire value is not a valid System.Decimal encoding.\", ex);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }
}
