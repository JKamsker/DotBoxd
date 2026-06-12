namespace SafeIR;

internal static class JsonExportNames
{
    public static string UnaryOperator(string op)
        => op switch
        {
            "!" => "not",
            "-" => "-",
            _ => throw Error("E-JSON-EXPORT", $"unary operator '{op}' cannot be exported")
        };

    public static string BinaryOperator(string op)
        => op switch
        {
            "+" => "add",
            "-" => "sub",
            "*" => "mul",
            "/" => "div",
            "%" => "rem",
            "==" => "eq",
            "!=" => "ne",
            "<" => "lt",
            "<=" => "lte",
            ">" => "gt",
            ">=" => "gte",
            "&&" => "and",
            "||" => "or",
            _ => throw Error("E-JSON-EXPORT", $"binary operator '{op}' cannot be exported")
        };

    public static string OpaqueIdName(string typeName)
        => typeName switch
        {
            "PlayerId" => "playerId",
            "ItemId" => "itemId",
            "QuestId" => "questId",
            "MapId" => "mapId",
            _ => throw Error("E-JSON-EXPORT", $"opaque ID type '{typeName}' cannot be exported")
        };

    public static SandboxValidationException Error(string code, string message)
        => new([new SandboxDiagnostic(code, message, Span: JsonImport.JsonSpan)]);
}
