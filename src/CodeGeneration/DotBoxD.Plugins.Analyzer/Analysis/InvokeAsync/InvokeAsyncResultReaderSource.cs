using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Plugins.Analyzer.Analysis.InvokeAsync;

internal sealed class InvokeAsyncResultReaderSource
{
    private readonly Dictionary<string, string> _readers = new(StringComparer.Ordinal);
    private readonly StringBuilder _helpers = new();
    private readonly string _helperPrefix;
    private int _nextHelper;

    public InvokeAsyncResultReaderSource(string helperPrefix = "ReadInvokeAsyncResult")
    {
        _helperPrefix = helperPrefix;
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
            SpecialType.System_String => $"{expression}.TextValue",
            _ => ReadComplexExpression(type, expression)
        };

    private string ReadComplexExpression(ITypeSymbol type, string expression)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(type) is not null)
        {
            return $"{EnsureListReader(type)}({expression})";
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
        var returnsArray = type is IArrayTypeSymbol;
        var returnType = returnsArray ? TypeName(type) : $"global::System.Collections.Generic.List<{elementName}>";
        _helpers.Append("        private static ").Append(returnType).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.List);");
        _helpers.AppendLine("            var __count = value.ItemCount;");
        AppendListReaderBody(elementName, itemExpression, returnsArray);
        _helpers.AppendLine();
        _helpers.AppendLine("            return __result;");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private void AppendListReaderBody(string elementName, string itemExpression, bool returnsArray)
    {
        if (returnsArray)
        {
            _helpers.Append("            var __result = new ").Append(elementName).AppendLine("[__count];");
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
        var arguments = DtoConstructorArguments(fields, ResolveConstructor(type, fields));
        _helpers.Append("        private static ").Append(TypeName(type)).Append(' ').Append(method)
            .AppendLine("(global::DotBoxD.Plugins.KernelRpcValue value)");
        _helpers.AppendLine("        {");
        _helpers.AppendLine("            value.RequireKind(global::DotBoxD.Plugins.KernelRpcValueKind.Record);");
        _helpers.Append("            if (value.ItemCount != ").Append(fields.Count).AppendLine(")");
        _helpers.AppendLine("            {");
        _helpers.AppendLine("                throw new global::System.NotSupportedException(\"Server extension record field count did not match the generated DTO shape.\");");
        _helpers.AppendLine("            }");
        _helpers.AppendLine();
        _helpers.Append("            return new ").Append(TypeName(type)).Append('(')
            .Append(string.Join(", ", arguments)).AppendLine(");");
        _helpers.AppendLine("        }");
        _helpers.AppendLine();
        return method;
    }

    private List<string> DtoConstructorArguments(IReadOnlyList<RecordMember> fields, IMethodSymbol constructor)
    {
        var arguments = new List<string>(constructor.Parameters.Length);
        foreach (var parameter in constructor.Parameters)
        {
            var fieldIndex = FieldIndex(fields, parameter.Name);
            arguments.Add(ReadExpression(fields[fieldIndex].Type, "value.GetItem(" + fieldIndex + ")"));
        }

        return arguments;
    }

    private static IMethodSymbol ResolveConstructor(INamedTypeSymbol type, IReadOnlyList<RecordMember> fields)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length != fields.Count || constructor.Parameters.Length == 0)
            {
                continue;
            }

            if (constructor.Parameters.All(parameter => FieldIndex(fields, parameter.Name) >= 0))
            {
                return constructor;
            }
        }

        throw new NotSupportedException(
            $"Server extension DTO '{type.ToDisplayString()}' must expose a constructor matching its public fields.");
    }

    private static int FieldIndex(IReadOnlyList<RecordMember> fields, string? name)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private string NextHelperName() => _helperPrefix + _nextHelper++;

    private static string TypeName(ITypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
