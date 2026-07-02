namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

/// <summary>
/// Map marshalling for <see cref="RpcKernelValueConversionEmitter"/>: a <c>Dictionary&lt;K,V&gt;</c> (or
/// <c>IReadOnlyDictionary</c>/<c>IDictionary</c>) is written as a <c>KernelRpcValue.Map</c> whose entries are
/// a flat key/value sequence, then read back into a mutable dictionary or a read-only wrapper matching the
/// declared contract. As with the DTO helpers, the nested key/value expressions are computed before the
/// owning helper's body is appended so a nested list/map/DTO helper is never spliced into the middle of the
/// method being built.
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
        _helpers.Append($"    private static {DotBoxDRpcValueNames.GlobalKernelRpcValue} ").Append(method)
            .Append('(').Append(TypeName(type)).AppendLine(" value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(value);");
        _helpers.AppendLine("        var __entryCount = value.Count * 2;");
        _helpers.AppendLine("        var __entries = __entryCount == 0")
            .AppendLine($"            ? global::System.Array.Empty<{DotBoxDRpcValueNames.GlobalKernelRpcValue}>()")
            .AppendLine($"            : new {DotBoxDRpcValueNames.GlobalKernelRpcValue}[__entryCount];");
        _helpers.AppendLine("        var __index = 0;");
        _helpers.AppendLine("        foreach (var __pair in value)");
        _helpers.AppendLine("        {");
        _helpers.Append("            __entries[__index++] = ").Append(keyExpression).AppendLine(";");
        _helpers.Append("            __entries[__index++] = ").Append(valueExpression).AppendLine(";");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        _helpers.AppendLine($"        return {DotBoxDRpcValueNames.GlobalKernelRpcValue}.Map(__entries);");
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
        var keyName = TypeName(keyType);
        var valueName = TypeName(valueType);
        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<{keyName}, {valueName}>";
        var returnType = DotBoxDRpcTypeMapper.IsReadOnlyMapShape(type) ? TypeName(type) : dictionaryType;

        // Compute the key/value expressions (which append nested helpers) BEFORE writing this method's body,
        // so a nested helper is never spliced into the middle of the reconstruction loop.
        var keyExpression = ReadExpression(keyType, "value.GetItem(i)");
        var valueExpression = ReadExpression(valueType, "value.GetItem(i + 1)");

        _helpers.Append("    private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine($"({DotBoxDRpcValueNames.GlobalKernelRpcValue} value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine($"        value.RequireKind({DotBoxDRpcValueNames.GlobalKernelRpcValueKind}.Map);");
        _helpers.AppendLine("        var __count = value.ItemCount;");
        _helpers.Append("        var __result = new ").Append(dictionaryType).AppendLine("(__count / 2);");
        _helpers.AppendLine("        for (var i = 0; i < __count; i += 2)");
        _helpers.AppendLine("        {");
        _helpers.Append("            var __key = ").Append(keyExpression).AppendLine(";");
        _helpers.AppendLine("            if (__result.ContainsKey(__key))");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.FormatException(\"Server extension map payload contains a duplicate key.\");");
        _helpers.AppendLine("            }");
        _helpers.Append("            __result.Add(__key, ").Append(valueExpression).AppendLine(");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        AppendMapReaderReturn(type, keyName, valueName);
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private void AppendMapReaderReturn(ITypeSymbol type, string keyName, string valueName)
    {
        if (DotBoxDRpcTypeMapper.IsReadOnlyMapShape(type))
        {
            _helpers.Append("        return new global::System.Collections.ObjectModel.ReadOnlyDictionary<")
                .Append(keyName).Append(", ").Append(valueName).AppendLine(">(__result);");
            return;
        }

        _helpers.AppendLine("        return __result;");
    }
}
