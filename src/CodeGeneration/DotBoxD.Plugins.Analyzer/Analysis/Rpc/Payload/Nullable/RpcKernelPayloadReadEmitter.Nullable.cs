namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelPayloadReadEmitter
{
    private string EnsureNullablePayloadReader(ITypeSymbol type, ITypeSymbol underlying)
    {
        var key = "nullable-reader:" + TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var valueExpression = ReadExpression(underlying, "reader");
        _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __fieldCount = reader.ReadRecordHeader();");
        _helpers.AppendLine("        if (__fieldCount != 2)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension nullable wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        var __hasValue = reader.ReadBool();");
        _helpers.Append("        var __value = ").Append(valueExpression).AppendLine(";");
        _helpers.AppendLine("        return __hasValue ? __value : default;");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }
}
