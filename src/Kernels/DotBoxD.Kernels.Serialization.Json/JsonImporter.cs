using System.Text.Json;
using DotBoxD.Kernels.Model;
using static DotBoxD.Kernels.Serialization.Json.JsonImport;

namespace DotBoxD.Kernels.Serialization.Json;

public static class JsonImporter
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

    /// <summary>
    /// Imports a module from an element that has already been parsed and budget-validated
    /// as part of a larger document (for example, the <c>module</c> subtree of a plugin
    /// package). Reusing the parsed element avoids a second <see cref="JsonDocument"/> parse
    /// and budget scan over the same payload. The source map is built from
    /// <paramref name="moduleJson"/> so spans stay relative to the module's own text,
    /// preserving the behaviour of <see cref="Import(string)"/>.
    /// </summary>
    internal static SandboxModule Import(JsonElement moduleElement, string moduleJson)
    {
        try
        {
            return ReadModule(moduleElement, JsonSourceMap.Create(moduleJson, moduleElement));
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
        if (count == 0)
        {
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
        if (count == 0)
        {
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
            ReadParameters(element, source),
            JsonExpressionReader.ReadType(Required(element, "returnType"), source),
            ReadStatements(RequiredArray(element, "body"), source));
    }

    private static IReadOnlyList<Parameter> ReadParameters(JsonElement function, JsonSourceMap source)
    {
        if (!function.TryGetProperty("parameters", out var array))
        {
            return [];
        }

        RequireArray(array, "parameters");
        var parameters = AllocateArray<Parameter>(array, out var count);
        if (count == 0)
        {
            return parameters;
        }

        var index = 0;
        foreach (var parameter in array.EnumerateArray())
        {
            RequireAllowedProperties(parameter, "parameter", ["name", "type"]);
            parameters[index++] = new Parameter(
                RequiredString(parameter, "name"),
                JsonExpressionReader.ReadType(Required(parameter, "type"), source));
        }

        return parameters;
    }

    private static IReadOnlyList<Statement> ReadStatements(JsonElement array, JsonSourceMap source)
    {
        var statements = AllocateArray<Statement>(array, out var count);
        if (count == 0)
        {
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
            "continue" => ReadContinueStatement(element, source),
            "break" => ReadBreakStatement(element, source),
            _ => throw Error("E-JSON-STATEMENT", $"unknown statement op '{op}'")
        };
    }

    private static ContinueStatement ReadContinueStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "continue statement", ["op"]);
        return new ContinueStatement(source.SpanFor(element));
    }

    private static BreakStatement ReadBreakStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "break statement", ["op"]);
        return new BreakStatement(source.SpanFor(element));
    }

    private static AssignmentStatement ReadSetStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "set statement", ["op", "name", "value"]);
        return new AssignmentStatement(
                RequiredString(element, "name"),
                JsonExpressionReader.ReadExpression(Required(element, "value"), source),
                source.SpanFor(element));
    }

    private static ReturnStatement ReadReturnStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "return statement", ["op", "value"]);
        return new ReturnStatement(JsonExpressionReader.ReadExpression(Required(element, "value"), source), source.SpanFor(element));
    }

    private static ExpressionStatement ReadExpressionStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "expression statement", ["op", "value"]);
        return new ExpressionStatement(JsonExpressionReader.ReadExpression(Required(element, "value"), source), source.SpanFor(element));
    }

    private static IfStatement ReadIfStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "if statement", ["op", "condition", "then", "else"]);
        return new IfStatement(
                JsonExpressionReader.ReadExpression(Required(element, "condition"), source),
                ReadStatements(RequiredArray(element, "then"), source),
                element.TryGetProperty("else", out var otherwise) ? ReadStatements(RequireArray(otherwise, "else"), source) : [],
                source.SpanFor(element));
    }

    private static WhileStatement ReadWhileStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "while statement", ["op", "condition", "body"]);
        return new WhileStatement(
                JsonExpressionReader.ReadExpression(Required(element, "condition"), source),
                ReadStatements(RequiredArray(element, "body"), source),
                source.SpanFor(element));
    }

    private static ForRangeStatement ReadForRangeStatement(JsonElement element, JsonSourceMap source)
    {
        RequireAllowedProperties(element, "forRange statement", ["op", "local", "start", "end", "body"]);
        return new ForRangeStatement(
                RequiredString(element, "local"),
                JsonExpressionReader.ReadExpression(Required(element, "start"), source),
                JsonExpressionReader.ReadExpression(Required(element, "end"), source),
                ReadStatements(RequiredArray(element, "body"), source),
                source.SpanFor(element));
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
