using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Serialization.Json.Internal;

using System.Text.Json;
using static JsonImport;

internal static class JsonExpressionReader
{
    public static Expression ReadExpression(JsonElement element, JsonSourceMap source)
    {
        RequireObject(element, "expression");
        if (element.TryGetProperty("var", out var variable))
        {
            RequireAllowedProperties(element, "variable expression", ["var"]);
            return new VariableExpression(ReadStringValue(variable, "var", source), source.SpanFor(element));
        }

        if (JsonLiteralReader.TryRead(element, source, out var literal, out var literalName))
        {
            RequireAllowedProperties(element, "literal expression", [literalName]);
            return new LiteralExpression(literal, source.SpanFor(element));
        }

        if (element.TryGetProperty("call", out var call))
        {
            RequireAllowedProperties(element, "call expression", ["call", "args", "genericType"]);
            return ReadCall(element, ReadStringValue(call, "call", source), source);
        }

        if (element.TryGetProperty("unary", out var unary))
        {
            RequireAllowedProperties(element, "unary expression", ["unary", "operand"]);
            return new UnaryExpression(
                JsonOperatorReader.NormalizeUnary(ReadStringValue(unary, "unary", source), unary, source),
                ReadExpression(Required(element, "operand"), source),
                source.SpanFor(element));
        }

        if (element.TryGetProperty("op", out var binary))
        {
            RequireAllowedProperties(element, "binary expression", ["op", "left", "right"]);
            return new BinaryExpression(
                ReadExpression(Required(element, "left"), source),
                JsonOperatorReader.NormalizeBinary(ReadStringValue(binary, "op", source), binary, source),
                ReadExpression(Required(element, "right"), source),
                source.SpanFor(element));
        }

        throw Error("E-JSON-EXPR", "unknown expression shape");
    }

    public static IReadOnlyList<Expression> ReadExpressions(JsonElement array, JsonSourceMap source)
    {
        RequireArray(array, "args");
        var expressions = AllocateArray<Expression>(array, out var count);
        if (count == 0)
        {
            return expressions;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            expressions[index++] = ReadExpression(item, source);
        }

        return expressions;
    }

    public static SandboxType ReadType(JsonElement element)
        => ReadType(element, source: null);

    public static SandboxType ReadType(JsonElement element, JsonSourceMap? source)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var name = element.GetString() ?? "";
            if (name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal))
            {
                throw Error("E-JSON-TYPE", "generic types must be JSON objects, not strings", SpanFor(source, element));
            }

            return SandboxType.Scalar(name);
        }

        RequireObject(element, "type");
        RequireAllowedProperties(element, "type", ["name", "arguments"]);
        var arguments = element.TryGetProperty("arguments", out var args)
            ? ReadTypeArguments(args, source)
            : [];
        return new SandboxType(ReadStringValue(Required(element, "name"), "name", source), arguments);
    }

    private static CallExpression ReadCall(JsonElement element, string name, JsonSourceMap source)
    {
        var args = element.TryGetProperty("args", out var array)
            ? ReadExpressions(array, source)
            : [];
        var genericType = element.TryGetProperty("genericType", out var generic)
            ? ReadType(generic, source)
            : null;
        return new CallExpression(name, args, genericType, source.SpanFor(element));
    }

    private static IReadOnlyList<SandboxType> ReadTypeArguments(JsonElement array, JsonSourceMap? source)
    {
        RequireArray(array, "type arguments");
        var arguments = AllocateArray<SandboxType>(array, out var count);
        if (count == 0)
        {
            return arguments;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            arguments[index++] = ReadType(item, source);
        }

        return arguments;
    }

    private static SourceSpan SpanFor(JsonSourceMap? source, JsonElement element)
        => source?.SpanFor(element) ?? JsonSpan;

    private static string ReadStringValue(JsonElement element, string name, JsonSourceMap? source)
        => source is null ? JsonImport.ReadStringValue(element, name) : JsonImport.ReadStringValue(element, name, source);
}
