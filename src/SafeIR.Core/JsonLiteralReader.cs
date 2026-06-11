namespace SafeIR;

using System.Text.Json;
using static JsonImport;

internal static class JsonLiteralReader
{
    public static bool TryRead(JsonElement element, out SandboxValue value, out string literalName)
    {
        if (element.TryGetProperty("i32", out var i32)) {
            value = SandboxValue.FromInt32(ReadInt32Value(i32, "i32"));
            literalName = "i32";
            return true;
        }

        if (element.TryGetProperty("i64", out var i64)) {
            value = SandboxValue.FromInt64(ReadInt64Value(i64, "i64"));
            literalName = "i64";
            return true;
        }

        if (element.TryGetProperty("f64", out var f64)) {
            value = SandboxValue.FromDouble(ReadDoubleValue(f64, "f64"));
            literalName = "f64";
            return true;
        }

        if (element.TryGetProperty("bool", out var boolean)) {
            value = SandboxValue.FromBool(ReadBoolValue(boolean, "bool"));
            literalName = "bool";
            return true;
        }

        if (element.TryGetProperty("string", out var text)) {
            value = SandboxValue.FromString(ReadStringValue(text, "string"));
            literalName = "string";
            return true;
        }

        if (element.TryGetProperty("path", out var path)) {
            value = SandboxValue.FromPath(ReadPathValue(path, "path"));
            literalName = "path";
            return true;
        }

        if (element.TryGetProperty("uri", out var uri)) {
            value = SandboxValue.FromUri(ReadUriValue(uri, "uri"));
            literalName = "uri";
            return true;
        }

        value = SandboxValue.Unit;
        literalName = "";
        return false;
    }
}
