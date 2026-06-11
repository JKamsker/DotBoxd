namespace SafeIR;

using System.Text.Json;
using static JsonImport;

internal static class JsonLiteralReader
{
    public static bool TryRead(JsonElement element, out SandboxValue value, out string literalName)
    {
        if (element.TryGetProperty("i32", out var i32)) {
            value = SandboxValue.FromInt32(i32.GetInt32());
            literalName = "i32";
            return true;
        }

        if (element.TryGetProperty("i64", out var i64)) {
            value = SandboxValue.FromInt64(i64.GetInt64());
            literalName = "i64";
            return true;
        }

        if (element.TryGetProperty("f64", out var f64)) {
            value = SandboxValue.FromDouble(f64.GetDouble());
            literalName = "f64";
            return true;
        }

        if (element.TryGetProperty("bool", out var boolean)) {
            value = SandboxValue.FromBool(boolean.GetBoolean());
            literalName = "bool";
            return true;
        }

        if (element.TryGetProperty("string", out var text)) {
            value = SandboxValue.FromString(ReadStringValue(text, "string"));
            literalName = "string";
            return true;
        }

        if (element.TryGetProperty("path", out var path)) {
            value = SandboxValue.FromPath(ReadStringValue(path, "path"));
            literalName = "path";
            return true;
        }

        if (element.TryGetProperty("uri", out var uri)) {
            value = SandboxValue.FromUri(ReadStringValue(uri, "uri"));
            literalName = "uri";
            return true;
        }

        value = SandboxValue.Unit;
        literalName = "";
        return false;
    }
}
