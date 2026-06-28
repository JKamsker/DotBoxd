namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// Emits the helper methods that marshal between a generated server-extension client's C# values and
/// <c>KernelRpcValue</c> wire values: scalars inline, <c>List&lt;T&gt;</c>/arrays as <c>List</c>, and DTOs
/// (records/structs/classes with public instance properties) as positional <c>Record</c>s. Field
/// expressions are computed before the owning helper is appended so a nested helper is never spliced into
/// the middle of another helper's body. Shared by the proxy and direct (graft) client emitters so both
/// support the same parameter and return shapes — DTO parameters and returns, nested DTOs, and
/// list-typed DTO fields — without divergence.
/// </summary>
internal sealed partial class RpcKernelValueConversionEmitter
{
    private readonly StringBuilder _helpers = new();
    private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _writers = new(StringComparer.Ordinal);
    private readonly Compilation? _compilation;
    private int _nextHelper;

    public RpcKernelValueConversionEmitter(Compilation? compilation = null)
    {
        _compilation = compilation;
    }

    /// <summary>The accumulated helper method definitions, appended after the emitter's own members.</summary>
    public string Helpers => _helpers.ToString();

    /// <summary>A C# expression that marshals <paramref name="expression"/> (of <paramref name="type"/>) into a <c>KernelRpcValue</c>.</summary>
    public string WriteExpression(ITypeSymbol type, string expression)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"global::DotBoxD.Plugins.KernelRpcValue.Bool({expression})",
            SpecialType.System_Int32 => $"global::DotBoxD.Plugins.KernelRpcValue.Int32({expression})",
            SpecialType.System_Int64 => $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression})",
            SpecialType.System_Double => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
            // float widens losslessly to the wire's only floating kind (F64); read narrows it back.
            SpecialType.System_Single => $"global::DotBoxD.Plugins.KernelRpcValue.Double({expression})",
            SpecialType.System_String => $"global::DotBoxD.Plugins.KernelRpcValue.String({expression})",
            _ => WriteComplexExpression(type, expression)
        };

    /// <summary>A C# expression that reads a <c>KernelRpcValue</c> (<paramref name="expression"/>) back into <paramref name="type"/>.</summary>
    public string ReadExpression(ITypeSymbol type, string expression)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{expression}.BoolValue",
            SpecialType.System_Int32 => $"{expression}.Int32Value",
            SpecialType.System_Int64 => $"{expression}.Int64Value",
            SpecialType.System_Double => $"{expression}.DoubleValue",
            SpecialType.System_Single => $"{EnsureSingleValueReader()}({expression})",
            SpecialType.System_String => $"{expression}.TextValue",
            _ => ReadComplexExpression(type, expression)
        };

    private string WriteComplexExpression(ITypeSymbol type, string expression)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Guid({expression})";
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return $"{EnsureDateTimeValueWriter(type)}({expression})";
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Int32({expression}.DayNumber)";
        }

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression}.Ticks)";
        }

        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
        {
            return $"global::DotBoxD.Plugins.KernelRpcValue.Int64({expression}.Ticks)";
        }

        if (DotBoxDRpcTypeMapper.IsIndexWireType(type))
        {
            return $"{EnsureIndexValueWriter()}({expression})";
        }

        if (DotBoxDRpcTypeMapper.IsRangeWireType(type))
        {
            return $"{EnsureRangeValueWriter()}({expression})";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return DotBoxDRpcTypeMapper.EnumUsesI64(enumType)
                ? $"global::DotBoxD.Plugins.KernelRpcValue.Int64(unchecked((long){expression}))"
                : $"global::DotBoxD.Plugins.KernelRpcValue.Int32(unchecked((int){expression}))";
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
        {
            return $"{EnsureListWriter(type)}({expression})";
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            return $"{EnsureMapWriter(type, map.Key, map.Value)}({expression})";
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return $"{EnsureDtoWriter(named)}({expression})";
        }

        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

    private string ReadComplexExpression(ITypeSymbol type, string expression)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return $"{expression}.GuidValue";
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return $"{EnsureDateTimeValueReader(type)}({expression})";
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
        {
            return $"{EnsureDateOnlyValueReader()}({expression}.Int32Value)";
        }

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type))
        {
            return $"{EnsureTimeOnlyValueReader()}({expression}.Int64Value)";
        }

        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
        {
            return $"new global::System.TimeSpan({expression}.Int64Value)";
        }

        if (DotBoxDRpcTypeMapper.IsIndexWireType(type))
        {
            return $"{EnsureIndexValueReader()}({expression})";
        }

        if (DotBoxDRpcTypeMapper.IsRangeWireType(type))
        {
            return $"{EnsureRangeValueReader()}({expression})";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return $"{EnsureEnumValueReader(enumType)}({expression})";
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
        {
            return $"{EnsureListReader(type)}({expression})";
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            return $"{EnsureMapReader(type, map.Key, map.Value)}({expression})";
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return $"{EnsureDtoReader(named)}({expression})";
        }

        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

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
        _helpers.Append("    private static global::DotBoxD.Plugins.KernelRpcValue ").Append(method)
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
            _helpers.Append("        var __items = new global::DotBoxD.Plugins.KernelRpcValue[")
                .Append(countExpression).AppendLine("];");
            _helpers.Append("        for (var i = 0; i < ").Append(countExpression).AppendLine("; i++)");
            _helpers.AppendLine("        {")
                .AppendLine("            var __item = value[i];");
            _helpers.Append("            __items[i] = ").Append(itemExpression).AppendLine(";");
            _helpers.AppendLine("        }")
                .AppendLine()
                .AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.List(__items);");
            return;
        }

        _helpers.AppendLine("        if (global::System.Linq.Enumerable.TryGetNonEnumeratedCount(value, out var __count))")
            .AppendLine("        {")
            .AppendLine("            var __items = new global::DotBoxD.Plugins.KernelRpcValue[__count];")
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
            .AppendLine("            return global::DotBoxD.Plugins.KernelRpcValue.List(__items);")
            .AppendLine("        }")
            .AppendLine();

        _helpers.AppendLine("        var __fallbackItems = new global::System.Collections.Generic.List<global::DotBoxD.Plugins.KernelRpcValue>();")
            .AppendLine("        foreach (var __item in value)")
            .AppendLine("        {");
        _helpers.Append("            __fallbackItems.Add(").Append(itemExpression).AppendLine(");");
        _helpers.AppendLine("        }")
            .AppendLine()
            .AppendLine("        return global::DotBoxD.Plugins.KernelRpcValue.List(__fallbackItems.ToArray());");
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
        var returnType = arrayType is not null ? TypeName(type) : $"global::System.Collections.Generic.List<{elementName}>";
        _helpers.Append("    private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.List);");
        _helpers.AppendLine("        var __count = value.ItemCount;");
        AppendListReaderBody(elementName, itemExpression, arrayType);
        _helpers.AppendLine();
        _helpers.AppendLine("        return __result;");
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

    private string NextHelperName(string prefix) => prefix + "KernelRpcValue" + _nextHelper++;

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string TypeKey(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Identifier(string name) => "@" + name;
}
