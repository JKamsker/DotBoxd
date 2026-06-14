namespace SafeIR.Serialization.Json.Internal;

using static JsonImport;

internal static class JsonOperatorReader
{
    public static string NormalizeUnary(string op)
        => op switch
        {
            "not" => "!",
            "-" => "-",
            _ => throw Error("E-JSON-OP", $"unknown unary op '{op}'")
        };

    public static string NormalizeBinary(string op)
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
            _ => throw Error("E-JSON-OP", $"unknown binary op '{op}'")
        };
}
