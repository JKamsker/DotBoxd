using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncResultReaderSource
{
    private string EnsureDateTimeValueReader(ITypeSymbol type)
    {
        var key = "datetime-reader:" + TypeName(type);
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
        var dateTimeMethod = NextHelperName();
        _readers[key] = dateTimeMethod;
        _helpers.Append("        private static global::System.DateTime ").Append(dateTimeMethod)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            return " + offsetReader + "(value).DateTime;");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return dateTimeMethod;
    }

    private string EnsureDateTimeOffsetValueReader()
    {
        const string key = "datetime-reader:System.DateTimeOffset";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var fromWire = EnsureDateTimeOffsetFromWireHelper();
        _helpers.Append("        private static global::System.DateTimeOffset ").Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
        _helpers.AppendLine("            if (value.ItemCount != 2)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync DateTime wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("            }");
        _helpers.AppendLine();
        _helpers.AppendLine("            return " + fromWire + "(value.GetItem(0).Int64Value, value.GetItem(1).Int64Value);");
        _helpers.AppendLine("        }");
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
        _helpers.Append("        private static global::System.DateTimeOffset ").Append(method)
            .AppendLine("(long utcTicks, long offsetTicks)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            try");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                var __offset = global::System.TimeSpan.FromTicks(offsetTicks);");
        _helpers.AppendLine("                return new global::System.DateTimeOffset(checked(utcTicks + offsetTicks), __offset);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("            catch (global::System.Exception ex) when (ex is global::System.ArgumentException or global::System.ArgumentOutOfRangeException or global::System.OverflowException)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync DateTime wire value is outside the supported DateTimeOffset range.\", ex);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private static bool IsDateTimeOffset(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "DateTimeOffset" };
}
