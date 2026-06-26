using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed partial class InvokeAsyncResultReaderSource
{
    private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
    private readonly StringBuilder _helpers = new();
    private readonly string _helperPrefix;
    private readonly Compilation? _compilation;
    private int _nextHelper;

    public InvokeAsyncResultReaderSource(
        string helperPrefix = "ReadInvokeAsyncResult",
        Compilation? compilation = null)
    {
        _helperPrefix = helperPrefix;
        _compilation = compilation;
    }

    public static (string Expression, string Helpers) Create(ITypeSymbol type, string expression)
    {
        var source = new InvokeAsyncResultReaderSource();
        return (source.ReadExpression(type, expression), source._helpers.ToString());
    }

    public string Helpers => _helpers.ToString();

    internal string ReadExpression(ITypeSymbol type, string expression)
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

    private string ReadComplexExpression(ITypeSymbol type, string expression)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return $"{expression}.GuidValue";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return $"{EnsureEnumValueReader(enumType)}({expression})";
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
        {
            return $"{EnsureListReader(type)}({expression})";
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is not null)
        {
            return $"{EnsureMapReader(type)}({expression})";
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return $"{EnsureDtoReader(named)}({expression})";
        }

        throw new NotSupportedException($"InvokeAsync return type '{type.ToDisplayString()}' is not supported.");
    }

    private string EnsureListReader(ITypeSymbol type)
    {
        var key = TypeName(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var elementType = DotBoxDRpcTypeMapper.ListElementType(type)!;
        var elementName = TypeName(elementType);
        var itemExpression = ReadExpression(elementType, "value.GetItem(i)");
        var arrayType = type as IArrayTypeSymbol;
        var returnType = arrayType is not null ? TypeName(type) : $"global::System.Collections.Generic.List<{elementName}>";
        _helpers.Append("        private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.List);");
        _helpers.AppendLine("            var __count = value.ItemCount;");
        AppendListReaderBody(elementName, itemExpression, arrayType);
        _helpers.AppendLine();
        _helpers.AppendLine("            return __result;");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private string EnsureMapReader(ITypeSymbol type)
    {
        var key = TypeName(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var map = DotBoxDRpcTypeMapper.MapTypes(type)
                  ?? throw new NotSupportedException($"InvokeAsync map return type '{type.ToDisplayString()}' is not supported.");
        var method = NextHelperName();
        _readers[key] = method;
        var keyName = TypeName(map.Key);
        var valueName = TypeName(map.Value);
        var keyExpression = ReadExpression(map.Key, "value.GetItem(i)");
        var valueExpression = ReadExpression(map.Value, "value.GetItem(i + 1)");
        _helpers.Append("        private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Map);");
        _helpers.AppendLine("            if ((value.ItemCount & 1) != 0)");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"InvokeAsync map result had an odd key/value entry count.\");");
        _helpers.AppendLine("            }");
        _helpers.Append("            var __result = new global::System.Collections.Generic.Dictionary<")
            .Append(keyName).Append(", ").Append(valueName).AppendLine(">(value.ItemCount / 2);");
        _helpers.AppendLine("            for (var i = 0; i < value.ItemCount; i += 2)");
        _helpers.AppendLine("            {");
        _helpers.Append("                __result[").Append(keyExpression).Append("] = ").Append(valueExpression).AppendLine(";");
        _helpers.AppendLine("            }");
        _helpers.AppendLine();
        _helpers.AppendLine("            return __result;");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private void AppendListReaderBody(string elementName, string itemExpression, IArrayTypeSymbol? arrayType)
    {
        if (arrayType is not null)
        {
            _helpers.Append("            var __result = ")
                .Append(ArrayCreation(arrayType, "__count"))
                .AppendLine(";");
            _helpers.AppendLine("            for (var i = 0; i < __count; i++)");
            _helpers.AppendLine("            {");
            _helpers.Append("                __result[i] = ").Append(itemExpression).AppendLine(";");
            _helpers.AppendLine("            }");
            return;
        }

        _helpers.Append("            var __result = new global::System.Collections.Generic.List<")
            .Append(elementName).AppendLine(">(__count);");
        _helpers.AppendLine("            for (var i = 0; i < __count; i++)");
        _helpers.AppendLine("            {");
        _helpers.Append("                __result.Add(").Append(itemExpression).AppendLine(");");
        _helpers.AppendLine("            }");
    }

    private string EnsureDtoReader(INamedTypeSymbol type)
    {
        var key = TypeName(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var fields = DotBoxDRpcTypeMapper.RecordFields(type);
        var fieldReads = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            fieldReads[i] = ReadExpression(fields[i].Type, "value.GetItem(" + i + ")");
        }

        var body = BuildDtoReconstruction(type, fields);
        _helpers.Append("        private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
        _helpers.Append("            if (value.ItemCount != ").Append(fields.Count).AppendLine(")");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated DTO shape.\");");
        _helpers.AppendLine("            }");
        _helpers.AppendLine();
        for (var i = 0; i < fields.Count; i++)
        {
            _helpers.Append("            var ").Append(FieldLocal(i)).Append(" = ")
                .Append(fieldReads[i]).AppendLine(";");
        }

        _helpers.AppendLine();
        _helpers.AppendLine(body);
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private string NextHelperName() => _helperPrefix + _nextHelper++;

    private static string ArrayCreation(IArrayTypeSymbol arrayType, string lengthExpression)
    {
        if (arrayType.Rank != 1)
        {
            throw new NotSupportedException(
                $"InvokeAsync multidimensional array return type '{arrayType.ToDisplayString()}' is not supported.");
        }

        var elementType = arrayType.ElementType;
        var trailingRanks = string.Empty;
        while (elementType is IArrayTypeSymbol nestedArray)
        {
            if (nestedArray.Rank != 1)
            {
                throw new NotSupportedException(
                    $"InvokeAsync multidimensional array return type '{nestedArray.ToDisplayString()}' is not supported.");
            }

            trailingRanks += "[]";
            elementType = nestedArray.ElementType;
        }

        return $"new {TypeName(elementType)}[{lengthExpression}]{trailingRanks}";
    }

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string FieldLocal(int index)
        => "__field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
