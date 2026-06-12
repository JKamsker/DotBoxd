using System.Text.Json;
using static SafeIR.JsonImport;

namespace SafeIR;

public static class SafeIrJsonImporter
{
    public static SandboxModule Import(string json)
    {
        try
        {
            JsonImportBudgetGuard.Validate(json);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });

            return ReadModule(document.RootElement, JsonSourceMap.Create(json, document.RootElement));
        }
        catch (JsonException ex)
        {
            throw Error("E-JSON-INVALID", ex.Message);
        }
        catch (FormatException ex)
        {
            throw Error("E-JSON-VERSION", ex.Message);
        }
    }

    private static SandboxModule ReadModule(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "module", ["id", "version", "targetSandboxVersion", "capabilityRequests", "functions", "metadata"]);
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
            ReadFunctions(element, source),
            ReadMetadata(element));
    }

    private static IReadOnlyList<CapabilityRequest> ReadCapabilityRequests(JsonElement module)
    {
        if (!module.TryGetProperty("capabilityRequests", out var array))
        {
            return [];
        }

        RequireArray(array, "capabilityRequests");
        var requests = AllocateArray<CapabilityRequest>(array, out var count);
        if (count == 0) {
            return requests;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            RequireAllowedProperties(item, "capability request", ["id", "reason"]);
            requests[index++] = new CapabilityRequest(RequiredString(item, "id"), OptionalString(item, "reason"));
        }

        return requests;
    }

    private static IReadOnlyList<SandboxFunction> ReadFunctions(JsonElement module, JsonSourceMap source)
    {
        var array = RequiredArray(module, "functions");
        var functions = AllocateArray<SandboxFunction>(array, out var count);
        if (count == 0) {
            return functions;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            functions[index++] = ReadFunction(item, source);
        }

        return functions;
    }

    private static SandboxFunction ReadFunction(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "function", ["id", "visibility", "parameters", "returnType", "body"]);
        RequireObject(element, "function");
        var visibility = OptionalString(element, "visibility") ?? "private";
        if (visibility is not "entrypoint" and not "private")
        {
            throw Error("E-JSON-VISIBILITY", $"unsupported function visibility '{visibility}'");
        }

        return new SandboxFunction(
            RequiredString(element, "id"),
            StringComparer.Ordinal.Equals(visibility, "entrypoint"),
            ReadParameters(element),
            ReadType(Required(element, "returnType")),
            ReadStatements(RequiredArray(element, "body"), source));
    }

    private static IReadOnlyList<Parameter> ReadParameters(JsonElement function)
    {
        if (!function.TryGetProperty("parameters", out var array))
        {
            return [];
        }

        RequireArray(array, "parameters");
        var parameters = AllocateArray<Parameter>(array, out var count);
        if (count == 0) {
            return parameters;
        }

        var index = 0;
        foreach (var parameter in array.EnumerateArray())
        {
            RequireAllowedProperties(parameter, "parameter", ["name", "type"]);
            parameters[index++] = new Parameter(
                RequiredString(parameter, "name"),
                ReadType(Required(parameter, "type")));
        }

        return parameters;
    }

    private static IReadOnlyList<Statement> ReadStatements(JsonElement array, JsonSourceMap source)
    {
        var statements = AllocateArray<Statement>(array, out var count);
        if (count == 0) {
            return statements;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            statements[index++] = ReadStatement(item, source);
        }

        return statements;
    }

    private static Statement ReadStatement(JsonElement element, JsonSourceMap source)
    {
        RequireObject(element, "statement");
        var op = RequiredString(element, "op");
        return op switch
        {
            "set" => ReadSetStatement(element, source),
            "return" => ReadReturnStatement(element, source),
            "expr" => ReadExpressionStatement(element, source),
            "if" => ReadIfStatement(element, source),
            "while" => ReadWhileStatement(element, source),
            "forRange" => ReadForRangeStatement(element, source),
            _ => throw Error("E-JSON-STATEMENT", $"unknown statement op '{op}'")
        };
    }

    private static AssignmentStatement ReadSetStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "set statement", ["op", "name", "value"]);
        return new AssignmentStatement(
                RequiredString(element, "name"),
                ReadExpression(Required(element, "value"), source),
                source.SpanFor(element));
    }

    private static ReturnStatement ReadReturnStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "return statement", ["op", "value"]);
        return new ReturnStatement(ReadExpression(Required(element, "value"), source), source.SpanFor(element));
    }

    private static ExpressionStatement ReadExpressionStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "expression statement", ["op", "value"]);
        return new ExpressionStatement(ReadExpression(Required(element, "value"), source), source.SpanFor(element));
    }

    private static IfStatement ReadIfStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "if statement", ["op", "condition", "then", "else"]);
        return new IfStatement(
                ReadExpression(Required(element, "condition"), source),
                ReadStatements(RequiredArray(element, "then"), source),
                element.TryGetProperty("else", out var otherwise) ? ReadStatements(RequireArray(otherwise, "else"), source) : [],
                source.SpanFor(element));
    }

    private static WhileStatement ReadWhileStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "while statement", ["op", "condition", "body"]);
        return new WhileStatement(
                ReadExpression(Required(element, "condition"), source),
                ReadStatements(RequiredArray(element, "body"), source),
                source.SpanFor(element));
    }

    private static ForRangeStatement ReadForRangeStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "forRange statement", ["op", "local", "start", "end", "body"]);
        return new ForRangeStatement(
                RequiredString(element, "local"),
                ReadExpression(Required(element, "start"), source),
                ReadExpression(Required(element, "end"), source),
                ReadStatements(RequiredArray(element, "body"), source),
                source.SpanFor(element));
    }

    private static Expression ReadExpression(JsonElement element, JsonSourceMap source)
    {
        RequireObject(element, "expression");
        if (element.TryGetProperty("var", out var variable))
        {
            RequireAllowedProperties(element, "variable expression", ["var"]);
            return new VariableExpression(ReadStringValue(variable, "var"), source.SpanFor(element));
        }

        if (JsonLiteralReader.TryRead(element, out var literal, out var literalName))
        {
            RequireAllowedProperties(element, "literal expression", [literalName]);
            return new LiteralExpression(literal, source.SpanFor(element));
        }

        if (element.TryGetProperty("call", out var call))
        {
            RequireAllowedProperties(element, "call expression", ["call", "args", "genericType"]);
            return ReadCall(element, ReadStringValue(call, "call"), source);
        }

        if (element.TryGetProperty("unary", out var unary))
        {
            RequireAllowedProperties(element, "unary expression", ["unary", "operand"]);
            return new UnaryExpression(
                JsonOperatorReader.NormalizeUnary(ReadStringValue(unary, "unary")),
                ReadExpression(Required(element, "operand"), source),
                source.SpanFor(element));
        }

        if (element.TryGetProperty("op", out var binary))
        {
            RequireAllowedProperties(element, "binary expression", ["op", "left", "right"]);
            return new BinaryExpression(
                ReadExpression(Required(element, "left"), source),
                JsonOperatorReader.NormalizeBinary(ReadStringValue(binary, "op")),
                ReadExpression(Required(element, "right"), source),
                source.SpanFor(element));
        }

        throw Error("E-JSON-EXPR", "unknown expression shape");
    }

    private static CallExpression ReadCall(JsonElement element, string name, JsonSourceMap source)
    {
        var args = element.TryGetProperty("args", out var array)
            ? ReadExpressions(array, source)
            : [];
        var genericType = element.TryGetProperty("genericType", out var generic)
            ? ReadType(generic)
            : null;
        return new CallExpression(name, args, genericType, source.SpanFor(element));
    }

    private static IReadOnlyList<Expression> ReadExpressions(JsonElement array, JsonSourceMap source)
    {
        RequireArray(array, "args");
        var expressions = AllocateArray<Expression>(array, out var count);
        if (count == 0) {
            return expressions;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            expressions[index++] = ReadExpression(item, source);
        }

        return expressions;
    }

    private static SandboxType ReadType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var name = element.GetString() ?? "";
            if (name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal))
            {
                throw Error("E-JSON-TYPE", "generic types must be JSON objects, not strings");
            }

            return SandboxType.Scalar(name);
        }

        RequireObject(element, "type");
        RequireAllowedProperties(element, "type", ["name", "arguments"]);
        var arguments = element.TryGetProperty("arguments", out var args)
            ? ReadTypeArguments(args)
            : [];
        return new SandboxType(RequiredString(element, "name"), arguments);
    }

    private static IReadOnlyList<SandboxType> ReadTypeArguments(JsonElement array)
    {
        RequireArray(array, "type arguments");
        var arguments = AllocateArray<SandboxType>(array, out var count);
        if (count == 0) {
            return arguments;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            arguments[index++] = ReadType(item);
        }

        return arguments;
    }
    private static IReadOnlyDictionary<string, string> ReadMetadata(JsonElement module)
    {
        if (!module.TryGetProperty("metadata", out var metadata))
        {
            return new Dictionary<string, string>();
        }

        RequireObject(metadata, "metadata");
        RequireUniqueProperties(metadata, "metadata");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in metadata.EnumerateObject())
        {
            values.Add(property.Name, ReadStringValue(property.Value, $"metadata.{property.Name}"));
        }

        return values;
    }

}
