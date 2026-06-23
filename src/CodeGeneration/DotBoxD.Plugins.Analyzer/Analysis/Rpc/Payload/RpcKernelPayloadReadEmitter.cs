namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// Emits readers that decode a known pushed <c>KernelRpcValue</c> binary payload directly into CLR values.
/// Generated <c>RunLocal</c> handlers use this to avoid materializing a full <c>KernelRpcValue</c> tree before
/// invoking the native delegate.
/// </summary>
internal sealed class RpcKernelPayloadReadEmitter
{
    private readonly StringBuilder _helpers = new();
    private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
    private int _nextHelper;

    public string Helpers => _helpers.ToString();

    public string ReadExpression(ITypeSymbol type, string reader)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{reader}.ReadBool()",
            SpecialType.System_Int32 => $"{reader}.ReadInt32()",
            SpecialType.System_Int64 => $"{reader}.ReadInt64()",
            SpecialType.System_Double => $"{reader}.ReadDouble()",
            SpecialType.System_Single => $"(float){reader}.ReadDouble()",
            SpecialType.System_String => $"{reader}.ReadString()",
            _ => ReadComplexExpression(type, reader)
        };

    private string ReadComplexExpression(ITypeSymbol type, string reader)
    {
        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return $"{reader}.ReadGuid()";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            var read = DotBoxDRpcTypeMapper.EnumUsesI64(enumType) ? "ReadInt64()" : "ReadInt32()";
            return $"({TypeName(type)}){reader}.{read}";
        }

        if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
        {
            return $"{EnsureListReader(type)}(ref {reader})";
        }

        if (DotBoxDRpcTypeMapper.MapTypes(type) is { } map)
        {
            return $"{EnsureMapReader(type, map.Key, map.Value)}(ref {reader})";
        }

        if (type is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return $"{EnsureDtoReader(named)}(ref {reader})";
        }

        throw new NotSupportedException($"Server extension type '{type.ToDisplayString()}' is not supported.");
    }

    private string EnsureListReader(ITypeSymbol type)
    {
        var key = TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var elementType = DotBoxDRpcTypeMapper.ListElementType(type)!;
        var elementName = TypeName(elementType);
        var itemExpression = ReadExpression(elementType, "reader");
        var arrayType = type as IArrayTypeSymbol;
        var returnType = arrayType is not null ? TypeName(type) : $"global::System.Collections.Generic.List<{elementName}>";
        _helpers.Append("    private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __count = reader.ReadListHeader();");
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

    private string EnsureMapReader(ITypeSymbol type, ITypeSymbol keyType, ITypeSymbol valueType)
    {
        var key = TypeKey(type);
        if (_readers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var method = NextHelperName();
        _readers[key] = method;
        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<{TypeName(keyType)}, {TypeName(valueType)}>";
        var keyExpression = ReadExpression(keyType, "reader");
        var valueExpression = ReadExpression(valueType, "reader");
        _helpers.Append("    private static ").Append(dictionaryType).Append(' ').Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __count = reader.ReadMapHeader();");
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

    private string EnsureDtoReader(INamedTypeSymbol type)
    {
        var key = TypeKey(type);
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
            fieldReads[i] = ReadExpression(fields[i].Type, "reader");
        }

        var body = RpcKernelPayloadDtoReaderBuilder.BuildReconstruction(type, fields);
        _helpers.Append("    private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        AppendRecordHeaderGuard(fields.Count);
        _helpers.AppendLine();
        for (var i = 0; i < fieldReads.Length; i++)
        {
            _helpers.Append("        var __field").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(" = ").Append(fieldReads[i]).AppendLine(";");
        }

        _helpers.AppendLine();
        _helpers.AppendLine(body);
        _helpers.AppendLine("    }");
        _helpers.AppendLine();
        return method;
    }

    public void AppendRecordHeaderGuard(StringBuilder builder, int fieldCount)
    {
        builder.AppendLine("        var __fieldCount = reader.ReadRecordHeader();");
        builder.Append("        if (__fieldCount != ").Append(fieldCount).AppendLine(")");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated projection shape.\");");
        builder.AppendLine("        }");
    }

    private void AppendRecordHeaderGuard(int fieldCount)
        => AppendRecordHeaderGuard(_helpers, fieldCount);

    private string NextHelperName() => "ReadKernelRpcPayload" + _nextHelper++;

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

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string TypeKey(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

}
