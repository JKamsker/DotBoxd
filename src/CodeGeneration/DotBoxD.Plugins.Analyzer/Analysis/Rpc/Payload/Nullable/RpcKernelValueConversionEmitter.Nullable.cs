namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private string EnsureNullableValueWriter(ITypeSymbol type, ITypeSymbol underlying)
    {
        var key = "nullable-writer:" + TypeKey(type);
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Write");
        _writers[key] = method;
        var valueExpression = WriteExpression(underlying, "value.GetValueOrDefault()");
        _helpers.Append($"    private static {DotBoxDRpcValueNames.GlobalKernelRpcValue} ").Append(method)
            .Append('(').Append(TypeName(type)).AppendLine(" value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine($"        return {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Record(new {DotBoxDRpcValueNames.GlobalKernelRpcValue}[]");
        _helpers.AppendLine("        {");
        _helpers.AppendLine($"            {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Bool(value.HasValue),");
        _helpers.Append("            ").Append(valueExpression).AppendLine(",");
        _helpers.AppendLine("        });");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureNullableValueReader(ITypeSymbol type, ITypeSymbol underlying)
    {
        var key = "nullable-reader:" + TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
        _readers[key] = method;
        var valueExpression = ReadExpression(underlying, "value.GetItem(1)");
        _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine($"({DotBoxDRpcValueNames.GlobalKernelRpcValue} value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine($"        value.RequireKind({DotBoxDRpcValueNames.GlobalKernelRpcValueKind}.Record);");
        _helpers.AppendLine("        if (value.ItemCount != 2)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension nullable wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        if (!value.GetItem(0).BoolValue)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            return default;");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.Append("        return ").Append(valueExpression).AppendLine(";");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }
}
