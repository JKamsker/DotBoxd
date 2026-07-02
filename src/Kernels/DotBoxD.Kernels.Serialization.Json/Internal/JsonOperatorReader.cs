namespace DotBoxD.Kernels.Serialization.Json.Internal;

using System.Text.Json;
using DotBoxD.Kernels.Model;
using static JsonImport;

internal static class JsonOperatorReader
{
    public static string NormalizeUnary(string op)
        => NormalizeUnary(op, JsonSpan);

    public static string NormalizeUnary(string op, SourceSpan span)
        => op switch
        {
            "not" => "!",
            "-" => "-",
            _ => throw Error("E-JSON-OP", $"unknown unary op '{op}'", span)
        };

    public static string NormalizeUnary(string op, JsonElement element, JsonSourceMap source)
        => op switch
        {
            "not" => "!",
            "-" => "-",
            _ => throw Error("E-JSON-OP", $"unknown unary op '{op}'", source.SpanFor(element))
        };

    public static string NormalizeBinary(string op)
        => NormalizeBinary(op, JsonSpan);

    public static string NormalizeBinary(string op, SourceSpan span)
        => op switch
        {
            "add" => "+",
            "sub" => "-",
            "mul" => "*",
            "div" => "/",
            "rem" => "%",
            "eq" => "==",
            "ne" => "!=",
            "lt" => "<",
            "lte" => "<=",
            "gt" => ">",
            "gte" => ">=",
            "and" => "&&",
            "or" => "||",
            _ => throw Error("E-JSON-OP", $"unknown binary op '{op}'", span)
        };

    public static string NormalizeBinary(string op, JsonElement element, JsonSourceMap source)
        => op switch
        {
            "add" => "+",
            "sub" => "-",
            "mul" => "*",
            "div" => "/",
            "rem" => "%",
            "eq" => "==",
            "ne" => "!=",
            "lt" => "<",
            "lte" => "<=",
            "gt" => ">",
            "gte" => ">=",
            "and" => "&&",
            "or" => "||",
            _ => throw Error("E-JSON-OP", $"unknown binary op '{op}'", source.SpanFor(element))
        };
}
