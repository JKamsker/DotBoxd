namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private string EnsureDateTimeValueWriter(ITypeSymbol type)
    {
        var key = "datetime-writer:" + TypeKey(type);
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (IsDateTimeOffset(type))
        {
            var method = EnsureDateTimeOffsetValueWriter();
            _writers[key] = method;
            return method;
        }

        var offsetWriter = EnsureDateTimeOffsetValueWriter();
        var dateTimeMethod = NextHelperName("Write");
        _writers[key] = dateTimeMethod;
        _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(dateTimeMethod)
            .AppendLine("(global::System.DateTime value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        return " + offsetWriter + "(DateTimeToWireOffset(value));");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        AppendDateTimeToWireOffsetHelper();
        return dateTimeMethod;
    }

    private string EnsureDateTimeValueReader(ITypeSymbol type)
    {
        var key = "datetime-reader:" + TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (IsDateTimeOffset(type))
        {
            var method = EnsureDateTimeOffsetValueReader();
            _readers[key] = method;
            return method;
        }

        var offsetReader = EnsureDateTimeOffsetValueReader();
        var dateTimeMethod = NextHelperName("Read");
        _readers[key] = dateTimeMethod;
        _helpers.Append("    private static global::System.DateTime ").Append(dateTimeMethod)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        return " + offsetReader + "(value).DateTime;");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return dateTimeMethod;
    }

    private string EnsureDateTimeOffsetValueWriter()
    {
        const string key = "datetime-writer:System.DateTimeOffset";
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Write");
        _writers[key] = method;
        AppendDateTimeOffsetWriter(method);
        return method;
    }

    private string EnsureDateTimeOffsetValueReader()
    {
        const string key = "datetime-reader:System.DateTimeOffset";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
        _readers[key] = method;
        AppendDateTimeOffsetValueReader(method);
        return method;
    }

    private void AppendDateTimeOffsetWriter(string method)
    {
        _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(method)
            .AppendLine("(global::System.DateTimeOffset value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.Record(new global::DotBoxD.Plugins.KernelRpcValue[]");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            global::DotBoxD.Plugins.KernelRpcValue.Int64(value.UtcTicks),");
        _helpers.AppendLine("            global::DotBoxD.Plugins.KernelRpcValue.Int64(value.Offset.Ticks),");
        _helpers.AppendLine("        });");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
    }

    private void AppendDateTimeOffsetValueReader(string method)
    {
        var fromWire = EnsureDateTimeOffsetFromWireHelper();
        _helpers.Append("    private static global::System.DateTimeOffset ").Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
        _helpers.AppendLine("        if (value.ItemCount != 2)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension DateTime wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        return " + fromWire + "(value.GetItem(0).Int64Value, value.GetItem(1).Int64Value);");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
    }

    private void AppendDateTimeToWireOffsetHelper()
    {
        const string key = "datetime-helper:to-offset";
        if (_writers.ContainsKey(key))
        {
            return;
        }

        _writers[key] = key;
        _helpers.AppendLine("    private static global::System.DateTimeOffset DateTimeToWireOffset(global::System.DateTime value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        return value.Kind == global::System.DateTimeKind.Local");
        _helpers.AppendLine("            ? new global::System.DateTimeOffset(value)");
        _helpers.AppendLine("            : new global::System.DateTimeOffset(");
        _helpers.AppendLine("                global::System.DateTime.SpecifyKind(value, global::System.DateTimeKind.Unspecified),");
        _helpers.AppendLine("                global::System.TimeSpan.Zero);");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
    }

    private string EnsureDateTimeOffsetFromWireHelper()
    {
        const string key = "datetime-helper:from-wire";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
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
