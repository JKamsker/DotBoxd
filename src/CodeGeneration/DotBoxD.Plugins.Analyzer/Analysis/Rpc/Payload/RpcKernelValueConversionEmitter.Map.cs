namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

/// <summary>
/// Map marshalling for <see cref="RpcKernelValueConversionEmitter"/>: a <c>Dictionary&lt;K,V&gt;</c> (or
/// <c>IReadOnlyDictionary</c>/<c>IDictionary</c>) is written as a <c>KernelRpcValue.Map</c> whose entries are
/// a flat key/value sequence, and read back into a concrete <c>Dictionary&lt;K,V&gt;</c>. As with the DTO
/// helpers, the nested key/value expressions are computed before the owning helper's body is appended so a
/// nested list/map/DTO helper is never spliced into the middle of the method being built.
/// </summary>
internal sealed partial class RpcKernelValueConversionEmitter
{
    private string EnsureMapWriter(ITypeSymbol type, ITypeSymbol keyType, ITypeSymbol valueType)
    {
        var key = TypeKey(type);
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Write");
        _writers[key] = method;
        var keyExpression = WriteExpression(keyType, "__pair.Key");
        var valueExpression = WriteExpression(valueType, "__pair.Value");
        _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(method)
            .Append('(').Append(TypeName(type)).AppendLine(" value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(value);");
        _helpers.AppendLine("        var __entries = new global::DotBoxD.Plugins.KernelRpcValue[value.Count * 2];");
        _helpers.AppendLine("        var __index = 0;");
        _helpers.AppendLine("        foreach (var __pair in value)");
        _helpers.AppendLine("        {");
        _helpers.Append("            __entries[__index++] = ").Append(keyExpression).AppendLine(";");
        _helpers.Append("            __entries[__index++] = ").Append(valueExpression).AppendLine(";");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.Map(__entries);");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureMapReader(ITypeSymbol type, ITypeSymbol keyType, ITypeSymbol valueType)
    {
        var key = TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
        _readers[key] = method;
        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<{TypeName(keyType)}, {TypeName(valueType)}>";

        // Compute the key/value expressions (which append nested helpers) BEFORE writing this method's body,
        // so a nested helper is never spliced into the middle of the reconstruction loop.
        var keyExpression = ReadExpression(keyType, "value.GetItem(i)");
        var valueExpression = ReadExpression(valueType, "value.GetItem(i + 1)");

        _helpers.Append("    private static ").Append(dictionaryType).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Map);");
        _helpers.AppendLine("        var __count = value.ItemCount;");
        _helpers.Append("        var __result = new ").Append(dictionaryType).AppendLine("(__count / 2);");
        _helpers.AppendLine("        for (var i = 0; i < __count; i += 2)");
        _helpers.AppendLine("        {");
        _helpers.Append("            __result[").Append(keyExpression).Append("] = ").Append(valueExpression)
            .AppendLine(";");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine("        return __result;");
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }
}
