using System.Text.Json;
using static SafeIR.JsonImport;

namespace SafeIR;

public static class SafeIrJsonImporter
{
    public static SandboxModule Import(string json)
    {
        try {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });

            return ReadModule(document.RootElement);
        }
        catch (JsonException ex) {
            throw Error("E-JSON-INVALID", ex.Message);
        }
        catch (FormatException ex) {
            throw Error("E-JSON-VERSION", ex.Message);
        }
    }

    private static SandboxModule ReadModule(JsonElement element)
    {
        RequireObject(element, "module root");
        var id = RequiredString(element, "id");
        var version = SemVersion.Parse(RequiredString(element, "version"));
        var target = OptionalString(element, "targetSandboxVersion") is { } targetText
            ? SemVersion.Parse(targetText)
            : SemVersion.One;

        return new SandboxModule(
            id,
            version,
            target,
            ReadCapabilityRequests(element),
            ReadFunctions(element),
            ReadMetadata(element));
    }

    private static IReadOnlyList<CapabilityRequest> ReadCapabilityRequests(JsonElement module)
    {
        if (!module.TryGetProperty("capabilityRequests", out var array)) {
            return [];
        }

        RequireArray(array, "capabilityRequests");
        return array.EnumerateArray()
            .Select(item => {
                RequireAllowedProperties(item, "capability request", ["id", "reason"]);
                return new CapabilityRequest(RequiredString(item, "id"), OptionalString(item, "reason"));
            })
            .ToArray();
    }

    private static IReadOnlyList<SandboxFunction> ReadFunctions(JsonElement module)
    {
        var array = RequiredArray(module, "functions");
        return array.EnumerateArray().Select(ReadFunction).ToArray();
    }

    private static SandboxFunction ReadFunction(JsonElement element)
    {
        RequireObject(element, "function");
        var visibility = OptionalString(element, "visibility") ?? "private";
        return new SandboxFunction(
            RequiredString(element, "id"),
            StringComparer.Ordinal.Equals(visibility, "entrypoint"),
            ReadParameters(element),
            ReadType(Required(element, "returnType")),
            ReadStatements(RequiredArray(element, "body")));
    }

    private static IReadOnlyList<Parameter> ReadParameters(JsonElement function)
    {
        if (!function.TryGetProperty("parameters", out var array)) {
            return [];
        }

        RequireArray(array, "parameters");
        return array.EnumerateArray()
            .Select(p => new Parameter(RequiredString(p, "name"), ReadType(Required(p, "type"))))
            .ToArray();
    }

    private static IReadOnlyList<Statement> ReadStatements(JsonElement array)
        => array.EnumerateArray().Select(ReadStatement).ToArray();

    private static Statement ReadStatement(JsonElement element)
    {
        RequireObject(element, "statement");
        var op = RequiredString(element, "op");
        return op switch {
            "set" => new AssignmentStatement(
                RequiredString(element, "name"),
                ReadExpression(Required(element, "value")),
                JsonSpan),
            "return" => new ReturnStatement(ReadExpression(Required(element, "value")), JsonSpan),
            "expr" => new ExpressionStatement(ReadExpression(Required(element, "value")), JsonSpan),
            "if" => new IfStatement(
                ReadExpression(Required(element, "condition")),
                ReadStatements(RequiredArray(element, "then")),
                element.TryGetProperty("else", out var otherwise) ? ReadStatements(otherwise) : [],
                JsonSpan),
            "while" => new WhileStatement(
                ReadExpression(Required(element, "condition")),
                ReadStatements(RequiredArray(element, "body")),
                JsonSpan),
            "forRange" => new ForRangeStatement(
                RequiredString(element, "local"),
                ReadExpression(Required(element, "start")),
                ReadExpression(Required(element, "end")),
                ReadStatements(RequiredArray(element, "body")),
                JsonSpan),
            _ => throw Error("E-JSON-STATEMENT", $"unknown statement op '{op}'")
        };
    }

    private static Expression ReadExpression(JsonElement element)
    {
        RequireObject(element, "expression");
        if (element.TryGetProperty("var", out var variable)) {
            return new VariableExpression(ReadStringValue(variable, "var"), JsonSpan);
        }

        if (TryReadLiteral(element, out var literal)) {
            return new LiteralExpression(literal, JsonSpan);
        }

        if (element.TryGetProperty("call", out var call)) {
            return ReadCall(element, ReadStringValue(call, "call"));
        }

        if (element.TryGetProperty("unary", out var unary)) {
            return new UnaryExpression(
                NormalizeOperator(ReadStringValue(unary, "unary")),
                ReadExpression(Required(element, "operand")),
                JsonSpan);
        }

        if (element.TryGetProperty("op", out var binary)) {
            return new BinaryExpression(
                ReadExpression(Required(element, "left")),
                NormalizeOperator(ReadStringValue(binary, "op")),
                ReadExpression(Required(element, "right")),
                JsonSpan);
        }

        throw Error("E-JSON-EXPR", "unknown expression shape");
    }

    private static bool TryReadLiteral(JsonElement element, out SandboxValue value)
    {
        if (element.TryGetProperty("i32", out var i32)) {
            value = SandboxValue.FromInt32(i32.GetInt32());
            return true;
        }

        if (element.TryGetProperty("i64", out var i64)) {
            value = SandboxValue.FromInt64(i64.GetInt64());
            return true;
        }

        if (element.TryGetProperty("f64", out var f64)) {
            value = SandboxValue.FromDouble(f64.GetDouble());
            return true;
        }

        if (element.TryGetProperty("bool", out var boolean)) {
            value = SandboxValue.FromBool(boolean.GetBoolean());
            return true;
        }

        if (element.TryGetProperty("string", out var text)) {
            value = SandboxValue.FromString(ReadStringValue(text, "string"));
            return true;
        }

        if (element.TryGetProperty("path", out var path)) {
            value = SandboxValue.FromPath(ReadStringValue(path, "path"));
            return true;
        }

        if (element.TryGetProperty("uri", out var uri)) {
            value = SandboxValue.FromUri(ReadStringValue(uri, "uri"));
            return true;
        }

        value = SandboxValue.Unit;
        return false;
    }

    private static CallExpression ReadCall(JsonElement element, string name)
    {
        var args = element.TryGetProperty("args", out var array)
            ? ReadExpressions(array)
            : [];
        var genericType = element.TryGetProperty("genericType", out var generic)
            ? ReadType(generic)
            : null;
        return new CallExpression(name, args, genericType, JsonSpan);
    }

    private static IReadOnlyList<Expression> ReadExpressions(JsonElement array)
    {
        RequireArray(array, "args");
        return array.EnumerateArray().Select(ReadExpression).ToArray();
    }

    private static SandboxType ReadType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String) {
            var name = element.GetString() ?? "";
            if (name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal)) {
                throw Error("E-JSON-TYPE", "generic types must be JSON objects, not strings");
            }

            return SandboxType.Scalar(name);
        }

        RequireObject(element, "type");
        var arguments = element.TryGetProperty("arguments", out var args)
            ? ReadTypeArguments(args)
            : [];
        return new SandboxType(RequiredString(element, "name"), arguments);
    }

    private static IReadOnlyList<SandboxType> ReadTypeArguments(JsonElement array)
    {
        RequireArray(array, "type arguments");
        return array.EnumerateArray().Select(ReadType).ToArray();
    }

    private static IReadOnlyDictionary<string, string> ReadMetadata(JsonElement module)
    {
        if (!module.TryGetProperty("metadata", out var metadata)) {
            return new Dictionary<string, string>();
        }

        RequireObject(metadata, "metadata");
        return metadata.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
    }

    private static string NormalizeOperator(string op)
        => op switch {
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
            "not" => "!",
            _ => op
        };

}
