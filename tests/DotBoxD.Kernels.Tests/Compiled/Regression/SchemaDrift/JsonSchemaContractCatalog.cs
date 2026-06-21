namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

internal static class JsonSchemaContractCatalog
{
    public static JsonSchemaObjectContract ForImporterShape(
        string requirementName,
        string[] allowedProperties)
        => new(requirementName, allowedProperties, RequiredPropertiesFor(requirementName))
        {
            ConstProperties = ConstPropertiesFor(requirementName),
            EnumProperties = EnumPropertiesFor(requirementName)
        };

    private static string[] RequiredPropertiesFor(string requirementName)
        => requirementName switch
        {
            "module" => ["id", "version", "functions"],
            "capability request" => ["id"],
            "function" => ["id", "returnType", "body"],
            "parameter" => ["name", "type"],
            "set statement" => ["op", "name", "value"],
            "return statement" => ["op", "value"],
            "expression statement" => ["op", "value"],
            "if statement" => ["op", "condition", "then"],
            "while statement" => ["op", "condition", "body"],
            "forRange statement" => ["op", "local", "start", "end", "body"],
            "continue statement" => ["op"],
            "break statement" => ["op"],
            "type" => ["name"],
            "variable expression" => ["var"],
            "call expression" => ["call"],
            "unary expression" => ["unary", "operand"],
            "binary expression" => ["op", "left", "right"],
            "plugin package" => ["manifest", "module"],
            "plugin manifest" => ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions"],
            "live setting" => ["name", "type", "defaultValue"],
            "hook subscription" => ["event", "kernel"],
            "indexed predicate" => ["path", "operator", "value", "valueType"],
            "kernel entrypoints" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(requirementName), requirementName, null)
        };

    private static IReadOnlyDictionary<string, string> ConstPropertiesFor(string requirementName)
        => StatementOpConst(requirementName) is { } op
            ? new Dictionary<string, string> { ["op"] = op }
            : new Dictionary<string, string>();

    private static string? StatementOpConst(string requirementName)
        => requirementName switch
        {
            "set statement" => "set",
            "return statement" => "return",
            "expression statement" => "expr",
            "if statement" => "if",
            "while statement" => "while",
            "forRange statement" => "forRange",
            "continue statement" => "continue",
            "break statement" => "break",
            _ => null
        };

    private static IReadOnlyDictionary<string, string[]> EnumPropertiesFor(string requirementName)
        => requirementName switch
        {
            "function" => new Dictionary<string, string[]>
            {
                ["visibility"] = ["entrypoint", "private"]
            },
            "unary expression" => new Dictionary<string, string[]>
            {
                ["unary"] = ["not", "-"]
            },
            "binary expression" => new Dictionary<string, string[]>
            {
                ["op"] = ["add", "sub", "mul", "div", "rem", "eq", "ne", "lt", "lte", "gt", "gte", "and", "or"]
            },
            "indexed predicate" => new Dictionary<string, string[]>
            {
                ["operator"] = ["Equals", "NotEquals", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual"],
                ["valueType"] = ["bool", "int", "long", "double", "string"]
            },
            _ => new Dictionary<string, string[]>()
        };
}
