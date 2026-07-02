namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using Microsoft.CodeAnalysis;

internal sealed partial class RpcKernelValueConversionEmitter
{
    private string EnsureListWriter(ITypeSymbol type)
    {
        var key = TypeKey(type);
        if (_writers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Write");
        _writers[key] = method;
        var elementType = DotBoxDRpcTypeMapper.ListElementType(type)!;
        var itemExpression = WriteExpression(elementType, "__item");
        _helpers.Append($"    private static {DotBoxDRpcValueNames.GlobalKernelRpcValue} ").Append(method)
            .Append('(').Append(TypeName(type)).AppendLine(" value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(value);");
        AppendListWriterBody(type, itemExpression);
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private void AppendListWriterBody(ITypeSymbol type, string itemExpression)
    {
        if (DotBoxDRpcTypeMapper.SupportsIndexedListWrite(type))
        {
            var countExpression = type is IArrayTypeSymbol ? "value.Length" : "value.Count";
            _helpers.Append("        var __count = ").Append(countExpression).AppendLine(";");
            _helpers.AppendLine("        var __items = __count == 0")
                .AppendLine($"            ? global::System.Array.Empty<{DotBoxDRpcValueNames.GlobalKernelRpcValue}>()")
                .AppendLine($"            : new {DotBoxDRpcValueNames.GlobalKernelRpcValue}[__count];");
            _helpers.AppendLine("        for (var i = 0; i < __count; i++)");
            _helpers.AppendLine("        {")
                .AppendLine("            var __item = value[i];");
            _helpers.Append("            __items[i] = ").Append(itemExpression).AppendLine(";");
            _helpers.AppendLine("        }")
                .AppendLine()
                .AppendLine($"        return {DotBoxDRpcValueNames.GlobalKernelRpcValue}.List(__items);");
            return;
        }

        _helpers.AppendLine("        if (global::System.Linq.Enumerable.TryGetNonEnumeratedCount(value, out var __count))")
            .AppendLine("        {")
            .AppendLine("            var __items = __count == 0")
            .AppendLine($"                ? global::System.Array.Empty<{DotBoxDRpcValueNames.GlobalKernelRpcValue}>()")
            .AppendLine($"                : new {DotBoxDRpcValueNames.GlobalKernelRpcValue}[__count];")
            .AppendLine("            var __index = 0;")
            .AppendLine("            foreach (var __item in value)")
            .AppendLine("            {")
            .AppendLine("                if (__index >= __items.Length)")
            .AppendLine("                {")
            .AppendLine("                    global::System.Array.Resize(ref __items, checked(__index + 1));")
            .AppendLine("                }")
            .AppendLine();
        _helpers.Append("                __items[__index++] = ").Append(itemExpression).AppendLine(";");
        _helpers.AppendLine("            }")
            .AppendLine()
            .AppendLine("            if (__index != __items.Length)")
            .AppendLine("            {")
            .AppendLine("                global::System.Array.Resize(ref __items, __index);")
            .AppendLine("            }")
            .AppendLine()
            .AppendLine($"            return {DotBoxDRpcValueNames.GlobalKernelRpcValue}.List(__items);")
            .AppendLine("        }")
            .AppendLine();

        _helpers.AppendLine($"        var __fallbackItems = new global::System.Collections.Generic.List<{DotBoxDRpcValueNames.GlobalKernelRpcValue}>();")
            .AppendLine("        foreach (var __item in value)")
            .AppendLine("        {");
        _helpers.Append("            __fallbackItems.Add(").Append(itemExpression).AppendLine(");");
        _helpers.AppendLine("        }")
            .AppendLine()
            .AppendLine($"        return {DotBoxDRpcValueNames.GlobalKernelRpcValue}.List(__fallbackItems.ToArray());");
    }

    private string EnsureListReader(ITypeSymbol type)
    {
        var key = TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName("Read");
        _readers[key] = method;
        var elementType = DotBoxDRpcTypeMapper.ListElementType(type)!;
        var elementName = TypeName(elementType);
        var itemExpression = ReadExpression(elementType, "value.GetItem(i)");
        var arrayType = type as IArrayTypeSymbol;
        var returnType = arrayType is not null ? TypeName(type) : ListReaderReturnType(type, elementName);
        _helpers.Append("    private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine($"({DotBoxDRpcValueNames.GlobalKernelRpcValue} value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine($"        value.RequireKind({DotBoxDRpcValueNames.GlobalKernelRpcValueKind}.List);");
        _helpers.AppendLine("        var __count = value.ItemCount;");
        AppendListReaderBody(elementName, itemExpression, arrayType);
        _helpers.AppendLine();
        AppendListReaderReturn(type, elementName, arrayType);
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    private void AppendListReaderBody(string elementName, string itemExpression, IArrayTypeSymbol? arrayType)
    {
        if (arrayType is not null)
        {
            _helpers.Append("        var __result = ")
                .Append(ArrayCreation(arrayType, "__count"))
                .AppendLine(";");
            _helpers.AppendLine("        for (var i = 0; i < __count; i++)");
            _helpers.AppendLine("        {");
            _helpers.Append("            __result[i] = ").Append(itemExpression).AppendLine(";");
            _helpers.AppendLine("        }");
            return;
        }

        _helpers.Append("        var __result = new global::System.Collections.Generic.List<")
            .Append(elementName).AppendLine(">(__count);");
        _helpers.AppendLine("        for (var i = 0; i < __count; i++)");
        _helpers.AppendLine("        {");
        _helpers.Append("            __result.Add(").Append(itemExpression).AppendLine(");");
        _helpers.AppendLine("        }");
    }

    private static string ListReaderReturnType(ITypeSymbol type, string elementName)
        => DotBoxDRpcTypeMapper.IsReadOnlyListShape(type)
            ? TypeName(type)
            : $"global::System.Collections.Generic.List<{elementName}>";

    private void AppendListReaderReturn(ITypeSymbol type, string elementName, IArrayTypeSymbol? arrayType)
    {
        if (arrayType is null && DotBoxDRpcTypeMapper.IsReadOnlyListShape(type))
        {
            _helpers.Append("        return new global::System.Collections.ObjectModel.ReadOnlyCollection<")
                .Append(elementName).AppendLine(">(__result);");
            return;
        }

        _helpers.AppendLine("        return __result;");
    }

    private static string ArrayCreation(IArrayTypeSymbol arrayType, string lengthExpression)
    {
        if (arrayType.Rank != 1)
        {
            throw new NotSupportedException(
                $"Server extension multidimensional array type '{arrayType.ToDisplayString()}' is not supported.");
        }

        var elementType = arrayType.ElementType;
        var trailingRanks = string.Empty;
        while (elementType is IArrayTypeSymbol nestedArray)
        {
            if (nestedArray.Rank != 1)
            {
                throw new NotSupportedException(
                    $"Server extension multidimensional array type '{nestedArray.ToDisplayString()}' is not supported.");
            }

            trailingRanks += "[]";
            elementType = nestedArray.ElementType;
        }

        return $"new {TypeName(elementType)}[{lengthExpression}]{trailingRanks}";
    }
}
