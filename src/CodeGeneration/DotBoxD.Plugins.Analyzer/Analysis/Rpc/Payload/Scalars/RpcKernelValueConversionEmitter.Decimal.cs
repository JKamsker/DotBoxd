namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private string EnsureDecimalValueWriter()
    {
        const string key = "decimal-writer:System.Decimal";
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Write");
        _writers[key] = method;
        _helpers.Append($"    private static {DotBoxDRpcValueNames.GlobalKernelRpcValue} ").Append(method)
            .AppendLine("(decimal value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __bits = global::System.Decimal.GetBits(value);");
        _helpers.AppendLine($"        return {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Record(new {DotBoxDRpcValueNames.GlobalKernelRpcValue}[]");
        _helpers.AppendLine("        {");
        _helpers.AppendLine($"            {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32(__bits[0]),");
        _helpers.AppendLine($"            {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32(__bits[1]),");
        _helpers.AppendLine($"            {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32(__bits[2]),");
        _helpers.AppendLine($"            {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Int32(__bits[3]),");
        _helpers.AppendLine("        });");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureDecimalValueReader()
    {
        const string key = "decimal-reader:System.Decimal";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
        _readers[key] = method;
        _helpers.Append("    private static decimal ").Append(method)
            .AppendLine($"({DotBoxDRpcValueNames.GlobalKernelRpcValue} value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine($"        value.RequireKind({DotBoxDRpcValueNames.GlobalKernelRpcValueKind}.Record);");
        _helpers.AppendLine("        if (value.ItemCount != 4)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension Decimal wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        try");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            return new decimal(new int[]");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                value.GetItem(0).Int32Value,");
        _helpers.AppendLine("                value.GetItem(1).Int32Value,");
        _helpers.AppendLine("                value.GetItem(2).Int32Value,");
        _helpers.AppendLine("                value.GetItem(3).Int32Value,");
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
