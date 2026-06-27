namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelPayloadReadEmitter
{
    private string EnsureDateTimePayloadReader(ITypeSymbol type)
    {
        var key = "datetime-reader:" + TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (IsDateTimeOffset(type))
        {
            var method = EnsureDateTimeOffsetPayloadReader();
            _readers[key] = method;
            return method;
        }

        var offsetReader = EnsureDateTimeOffsetPayloadReader();
        var dateTimeMethod = NextHelperName();
        _readers[key] = dateTimeMethod;
        _helpers.Append("    private static global::System.DateTime ").Append(dateTimeMethod)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        return " + offsetReader + "(ref reader).DateTime;");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return dateTimeMethod;
    }

    private string EnsureDateTimeOffsetPayloadReader()
    {
        const string key = "datetime-reader:System.DateTimeOffset";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var fromWire = EnsureDateTimeOffsetFromWireHelper();
        _helpers.Append("    private static global::System.DateTimeOffset ").Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __fieldCount = reader.ReadRecordHeader();");
        _helpers.AppendLine("        if (__fieldCount != 2)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension DateTime wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        return " + fromWire + "(reader.ReadInt64(), reader.ReadInt64());");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureDateTimeOffsetFromWireHelper()
    {
        const string key = "datetime-helper:from-wire";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("    private static global::System.DateTimeOffset ").Append(method)
            .AppendLine("(long utcTicks, long offsetTicks)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        try");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            var __offset = global::System.TimeSpan.FromTicks(offsetTicks);");
        _helpers.AppendLine("            return new global::System.DateTimeOffset(checked(utcTicks + offsetTicks), __offset);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("        catch (global::System.Exception ex) when (ex is global::System.ArgumentException or global::System.ArgumentOutOfRangeException or global::System.OverflowException)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension DateTime wire value is outside the supported DateTimeOffset range.\", ex);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private static bool IsDateTimeOffset(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "DateTimeOffset" };
}
