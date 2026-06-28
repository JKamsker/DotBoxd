namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class RpcKernelPayloadReadEmitter
{
    private string EnsureDateOnlyPayloadReader()
    {
        const string key = "dateonly-reader:System.DateOnly";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("    private static global::System.DateOnly ").Append(method).AppendLine("(int dayNumber)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        try");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            return global::System.DateOnly.FromDayNumber(dayNumber);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("        catch (global::System.ArgumentOutOfRangeException ex)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension DateOnly wire value is outside the supported DateOnly range.\", ex);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureTimeOnlyPayloadReader()
    {
        const string key = "timeonly-reader:System.TimeOnly";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("    private static global::System.TimeOnly ").Append(method).AppendLine("(long ticks)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        try");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            return new global::System.TimeOnly(ticks);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("        catch (global::System.ArgumentOutOfRangeException ex)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension TimeOnly wire value is outside the supported TimeOnly range.\", ex);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureIndexPayloadReader()
    {
        const string key = "index-reader:System.Index";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("    private static global::System.Index ").Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __fieldCount = reader.ReadRecordHeader();");
        _helpers.AppendLine("        if (__fieldCount != 2)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension Index wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        try");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            return new global::System.Index(reader.ReadInt32(), reader.ReadBool());");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("        catch (global::System.ArgumentOutOfRangeException ex)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension Index wire value is outside the supported Index range.\", ex);");
        _helpers.AppendLine("        }");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureRangePayloadReader()
    {
        const string key = "range-reader:System.Range";
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var indexReader = EnsureIndexPayloadReader();
        var method = NextHelperName();
        _readers[key] = method;
        _helpers.Append("    private static global::System.Range ").Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __fieldCount = reader.ReadRecordHeader();");
        _helpers.AppendLine("        if (__fieldCount != 2)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.NotSupportedException(\"Server extension Range wire value field count did not match the generated projection shape.\");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        return new global::System.Range(" + indexReader + "(ref reader), " + indexReader + "(ref reader));");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }
}
