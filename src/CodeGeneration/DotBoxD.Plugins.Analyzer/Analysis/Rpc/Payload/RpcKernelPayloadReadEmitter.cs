namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

/// <summary>
/// Emits readers that decode a known pushed <c>KernelRpcValue</c> binary payload directly into CLR values.
/// Generated <c>RunLocal</c> handlers use this to avoid materializing a full <c>KernelRpcValue</c> tree before
/// invoking the native delegate.
/// </summary>
internal sealed partial class RpcKernelPayloadReadEmitter
{
    private readonly StringBuilder _helpers = new();
    private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
    private readonly Compilation? _compilation;
    private int _nextHelper;

    public RpcKernelPayloadReadEmitter(Compilation? compilation = null)
    {
        _compilation = compilation;
    }

    public string Helpers => _helpers.ToString();

    public string ReadExpression(ITypeSymbol type, string reader)
        => type.SpecialType switch
        {
            SpecialType.System_Boolean => $"{reader}.ReadBool()",
            SpecialType.System_Int32 => $"{reader}.ReadInt32()",
            SpecialType.System_Int64 => $"{reader}.ReadInt64()",
            SpecialType.System_Double => $"{reader}.ReadDouble()",
            SpecialType.System_Single => $"{EnsureSingleReader()}(ref {reader})",
            SpecialType.System_String => $"{reader}.ReadString()",
            _ => ReadComplexExpression(type, reader)
        };

    private string ReadComplexExpression(ITypeSymbol type, string reader)
    {
        if (DotBoxDNullableScalarType.TryGetSupportedUnderlying(type, out var nullableUnderlying))
        {
            return $"{EnsureNullablePayloadReader(type, nullableUnderlying)}(ref {reader})";
        }

        if (DotBoxDRpcTypeMapper.IsGuid(type))
        {
            return $"{reader}.ReadGuid()";
        }

        if (DotBoxDRpcTypeMapper.IsDateTimeWireType(type))
        {
            return $"{EnsureDateTimePayloadReader(type)}(ref {reader})";
        }

        if (DotBoxDRpcTypeMapper.IsDecimalWireType(type))
        {
            return $"{EnsureDecimalPayloadReader()}(ref {reader})";
        }

        if (DotBoxDRpcTypeMapper.IsDateOnlyWireType(type))
        {
            return $"{EnsureDateOnlyPayloadReader()}({reader}.ReadInt32())";
        }

        if (DotBoxDRpcTypeMapper.IsTimeOnlyWireType(type))
        {
            return $"{EnsureTimeOnlyPayloadReader()}({reader}.ReadInt64())";
        }

        if (DotBoxDRpcTypeMapper.IsTimeSpanWireType(type))
        {
            return $"new global::System.TimeSpan({reader}.ReadInt64())";
        }

        if (DotBoxDRpcTypeMapper.IsCancellationTokenWireType(type))
        {
            return $"new global::System.Threading.CancellationToken({reader}.ReadBool())";
        }

        if (DotBoxDRpcTypeMapper.IsIndexWireType(type))
        {
            return $"{EnsureIndexPayloadReader()}(ref {reader})";
        }

        if (DotBoxDRpcTypeMapper.IsRangeWireType(type))
        {
            return $"{EnsureRangePayloadReader()}(ref {reader})";
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            return $"{EnsureEnumReader(enumType)}(ref {reader})";
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
        var readOnlyList = arrayType is null && DotBoxDRpcTypeMapper.IsReadOnlyListShape(type);
        var returnType = arrayType is not null
            ? TypeName(type)
            : readOnlyList ? TypeName(type) : $"global::System.Collections.Generic.List<{elementName}>";
        var returnExpression = readOnlyList
            ? $"new global::System.Collections.ObjectModel.ReadOnlyCollection<{elementName}>(__result)"
            : "__result";
        _helpers.Append("    private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __count = reader.ReadListHeader();");
        AppendListReaderBody(elementName, itemExpression, arrayType);
        _helpers.AppendLine();
        _helpers.Append("        return ").Append(returnExpression).AppendLine(";");
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
        var keyName = TypeName(keyType);
        var valueName = TypeName(valueType);
        var dictionaryType =
            $"global::System.Collections.Generic.Dictionary<{keyName}, {valueName}>";
        var readOnlyMap = DotBoxDRpcTypeMapper.IsReadOnlyMapShape(type);
        var returnType = readOnlyMap ? TypeName(type) : dictionaryType;
        var returnExpression = readOnlyMap
            ? $"new global::System.Collections.ObjectModel.ReadOnlyDictionary<{keyName}, {valueName}>(__result)"
            : "__result";
        var keyExpression = ReadExpression(keyType, "reader");
        var valueExpression = ReadExpression(valueType, "reader");
        _helpers.Append("    private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine("(ref global::DotBoxD.Plugins.KernelRpcPayloadReader reader)");
        _helpers.AppendLine("    {");
        _helpers.AppendLine("        var __count = reader.ReadMapHeader();");
        _helpers.AppendLine("        if ((__count & 1) != 0)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            throw new global::System.FormatException(\"Server extension map payload must contain key/value pairs.\");");
        _helpers.AppendLine("        }");
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
        _helpers.Append("        return ").Append(returnExpression).AppendLine(";");
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

        var body = RpcKernelPayloadDtoReaderBuilder.BuildReconstruction(type, fields, _compilation);
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
