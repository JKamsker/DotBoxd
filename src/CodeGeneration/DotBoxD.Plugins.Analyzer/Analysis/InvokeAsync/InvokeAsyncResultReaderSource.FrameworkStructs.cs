using DotBoxD.Plugins.Analyzer.Analysis.Rpc;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncResultReaderSource
{
    private string EnsureDateOnlyValueReader()
    {
        const string key = "dateonly-reader:System.DateOnly";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("        private static global::System.DateOnly ").Append(method)
            .AppendLine("(int dayNumber)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            try");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                return global::System.DateOnly.FromDayNumber(dayNumber);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("            catch (global::System.ArgumentOutOfRangeException ex)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync DateOnly wire value is outside the supported DateOnly range.\", ex);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureTimeOnlyValueReader()
    {
        const string key = "timeonly-reader:System.TimeOnly";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("        private static global::System.TimeOnly ").Append(method)
            .AppendLine("(long ticks)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            try");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                return new global::System.TimeOnly(ticks);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("            catch (global::System.ArgumentOutOfRangeException ex)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync TimeOnly wire value is outside the supported TimeOnly range.\", ex);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureIndexValueReader()
    {
        const string key = "index-reader:System.Index";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("        private static global::System.Index ").Append(method)
            .AppendLine($"({DotBoxDRpcValueNames.GlobalKernelRpcValue} value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine($"            value.RequireKind({DotBoxDRpcValueNames.GlobalKernelRpcValueKind}.Record);");
        _helpers.AppendLine("            if (value.ItemCount != 2)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync Index wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("            }");
        _helpers.AppendLine();
        _helpers.AppendLine("            try");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                return new global::System.Index(value.GetItem(0).Int32Value, value.GetItem(1).BoolValue);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("            catch (global::System.ArgumentOutOfRangeException ex)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync Index wire value is outside the supported Index range.\", ex);");
        _helpers.AppendLine("            }");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureRangeValueReader()
    {
        const string key = "range-reader:System.Range";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var indexReader = EnsureIndexValueReader();
        _helpers.Append("        private static global::System.Range ").Append(method)
            .AppendLine($"({DotBoxDRpcValueNames.GlobalKernelRpcValue} value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine($"            value.RequireKind({DotBoxDRpcValueNames.GlobalKernelRpcValueKind}.Record);");
        _helpers.AppendLine("            if (value.ItemCount != 2)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync Range wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("            }");
        _helpers.AppendLine();
        _helpers.AppendLine("            return new global::System.Range(" + indexReader + "(value.GetItem(0)), " + indexReader + "(value.GetItem(1)));");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }
}
