namespace DotBoxd.Kernels;

using System.Buffers;
using System.Text;
using System.Text.Json;

public static class DotBoxdJsonExporter
{
    public static string Export(SandboxModule module, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(module);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented });
        Write(writer, module);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal static void Write(Utf8JsonWriter writer, SandboxModule module)
    {
        writer.WriteStartObject();
        writer.WriteString("id", module.Id);
        writer.WriteString("version", module.Version.ToString());
        writer.WriteString("targetSandboxVersion", module.TargetSandboxVersion.ToString());
        WriteCapabilityRequests(writer, module.CapabilityRequests);
        WriteMetadata(writer, module.Metadata);
        WriteFunctions(writer, module.Functions);
        writer.WriteEndObject();
    }

    private static void WriteCapabilityRequests(Utf8JsonWriter writer, IReadOnlyList<CapabilityRequest> requests)
    {
        writer.WritePropertyName("capabilityRequests");
        writer.WriteStartArray();
        foreach (var request in requests) {
            writer.WriteStartObject();
            writer.WriteString("id", request.Id);
            if (request.Reason is not null) {
                writer.WriteString("reason", request.Reason);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteMetadata(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> metadata)
    {
        writer.WritePropertyName("metadata");
        writer.WriteStartObject();
        foreach (var item in metadata.OrderBy(item => item.Key, StringComparer.Ordinal)) {
            writer.WriteString(item.Key, item.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteFunctions(Utf8JsonWriter writer, IReadOnlyList<SandboxFunction> functions)
    {
        writer.WritePropertyName("functions");
        writer.WriteStartArray();
        foreach (var function in functions) {
            WriteFunction(writer, function);
        }

        writer.WriteEndArray();
    }

    private static void WriteFunction(Utf8JsonWriter writer, SandboxFunction function)
    {
        writer.WriteStartObject();
        writer.WriteString("id", function.Id);
        writer.WriteString("visibility", function.IsEntrypoint ? "entrypoint" : "private");
        WriteParameters(writer, function.Parameters);
        writer.WritePropertyName("returnType");
        WriteType(writer, function.ReturnType);
        writer.WritePropertyName("body");
        WriteStatements(writer, function.Body);
        writer.WriteEndObject();
    }

    private static void WriteParameters(Utf8JsonWriter writer, IReadOnlyList<Parameter> parameters)
    {
        writer.WritePropertyName("parameters");
        writer.WriteStartArray();
        foreach (var parameter in parameters) {
            writer.WriteStartObject();
            writer.WriteString("name", parameter.Name);
            writer.WritePropertyName("type");
            WriteType(writer, parameter.Type);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStatements(Utf8JsonWriter writer, IReadOnlyList<Statement> statements)
    {
        writer.WriteStartArray();
        foreach (var statement in statements) {
            WriteStatement(writer, statement);
        }

        writer.WriteEndArray();
    }

    private static void WriteStatement(Utf8JsonWriter writer, Statement statement)
    {
        writer.WriteStartObject();
        switch (statement) {
            case AssignmentStatement assignment:
                writer.WriteString("op", "set");
                writer.WriteString("name", assignment.Name);
                writer.WritePropertyName("value");
                WriteExpression(writer, assignment.Value);
                break;
            case ReturnStatement ret:
                writer.WriteString("op", "return");
                writer.WritePropertyName("value");
                WriteExpression(writer, ret.Value);
                break;
            case ExpressionStatement expression:
                writer.WriteString("op", "expr");
                writer.WritePropertyName("value");
                WriteExpression(writer, expression.Value);
                break;
            case IfStatement branch:
                writer.WriteString("op", "if");
                writer.WritePropertyName("condition");
                WriteExpression(writer, branch.Condition);
                writer.WritePropertyName("then");
                WriteStatements(writer, branch.Then);
                writer.WritePropertyName("else");
                WriteStatements(writer, branch.Else);
                break;
            case WhileStatement loop:
                writer.WriteString("op", "while");
                writer.WritePropertyName("condition");
                WriteExpression(writer, loop.Condition);
                writer.WritePropertyName("body");
                WriteStatements(writer, loop.Body);
                break;
            case ForRangeStatement range:
                writer.WriteString("op", "forRange");
                writer.WriteString("local", range.LocalName);
                writer.WritePropertyName("start");
                WriteExpression(writer, range.Start);
                writer.WritePropertyName("end");
                WriteExpression(writer, range.End);
                writer.WritePropertyName("body");
                WriteStatements(writer, range.Body);
                break;
            default:
                throw Error("E-JSON-EXPORT", $"statement type '{statement.GetType().Name}' cannot be exported");
        }

        writer.WriteEndObject();
    }

    private static void WriteExpression(Utf8JsonWriter writer, Expression expression)
    {
        writer.WriteStartObject();
        switch (expression) {
            case VariableExpression variable:
                writer.WriteString("var", variable.Name);
                break;
            case LiteralExpression literal:
                WriteLiteral(writer, literal.Value);
                break;
            case CallExpression call:
                WriteCall(writer, call);
                break;
            case UnaryExpression unary:
                writer.WriteString("unary", JsonExportNames.UnaryOperator(unary.Operator));
                writer.WritePropertyName("operand");
                WriteExpression(writer, unary.Operand);
                break;
            case BinaryExpression binary:
                writer.WriteString("op", JsonExportNames.BinaryOperator(binary.Operator));
                writer.WritePropertyName("left");
                WriteExpression(writer, binary.Left);
                writer.WritePropertyName("right");
                WriteExpression(writer, binary.Right);
                break;
            default:
                throw Error("E-JSON-EXPORT", $"expression type '{expression.GetType().Name}' cannot be exported");
        }

        writer.WriteEndObject();
    }

    private static void WriteCall(Utf8JsonWriter writer, CallExpression call)
    {
        writer.WriteString("call", call.Name);
        if (call.GenericType is not null) {
            writer.WritePropertyName("genericType");
            WriteType(writer, call.GenericType);
        }

        writer.WritePropertyName("args");
        writer.WriteStartArray();
        foreach (var argument in call.Arguments) {
            WriteExpression(writer, argument);
        }

        writer.WriteEndArray();
    }

    private static void WriteLiteral(Utf8JsonWriter writer, SandboxValue value)
    {
        switch (value) {
            case UnitValue:
                writer.WriteBoolean("unit", true);
                break;
            case BoolValue boolean:
                writer.WriteBoolean("bool", boolean.Value);
                break;
            case I32Value integer:
                writer.WriteNumber("i32", integer.Value);
                break;
            case I64Value integer:
                writer.WriteNumber("i64", integer.Value);
                break;
            case F64Value number:
                writer.WriteNumber("f64", number.Value);
                break;
            case StringValue text:
                writer.WriteString("string", text.Value);
                break;
            case OpaqueIdValue id:
                writer.WriteStartObject("opaqueId");
                writer.WriteString("type", id.TypeName);
                writer.WriteString("value", id.Value);
                writer.WriteEndObject();
                break;
            case SandboxPathValue path:
                writer.WriteString("path", path.Value.RelativePath);
                break;
            case SandboxUriValue uri:
                writer.WriteString("uri", uri.Value.Value);
                break;
            default:
                throw JsonExportNames.Error("E-JSON-EXPORT", $"literal type '{value.GetType().Name}' cannot be exported");
        }
    }

    private static void WriteType(Utf8JsonWriter writer, SandboxType type)
    {
        if (type.Arguments.Count == 0) {
            writer.WriteStringValue(type.Name);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("name", type.Name);
        writer.WritePropertyName("arguments");
        writer.WriteStartArray();
        foreach (var argument in type.Arguments) {
            WriteType(writer, argument);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static SandboxValidationException Error(string code, string message) => JsonExportNames.Error(code, message);
}
