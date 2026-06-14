namespace DotBoxd.Kernels.Serialization.Json.Internal;

using System.Text.Json;
using static JsonImport;

internal static class JsonLiteralReader
{
    public static bool TryRead(JsonElement element, out SandboxValue value, out string literalName)
    {
        if (element.TryGetProperty("unit", out var unit))
        {
            if (!ReadBoolValue(unit, "unit"))
            {
                throw Error("E-JSON-TYPE", "'unit' must be true");
            }

            value = SandboxValue.Unit;
            literalName = "unit";
            return true;
        }

        if (element.TryGetProperty("i32", out var i32))
        {
            value = SandboxValue.FromInt32(ReadInt32Value(i32, "i32"));
            literalName = "i32";
            return true;
        }

        if (element.TryGetProperty("i64", out var i64))
        {
            value = SandboxValue.FromInt64(ReadInt64Value(i64, "i64"));
            literalName = "i64";
            return true;
        }

        if (element.TryGetProperty("f64", out var f64))
        {
            value = SandboxValue.FromDouble(ReadDoubleValue(f64, "f64"));
            literalName = "f64";
            return true;
        }

        if (element.TryGetProperty("bool", out var boolean))
        {
            value = SandboxValue.FromBool(ReadBoolValue(boolean, "bool"));
            literalName = "bool";
            return true;
        }

        if (element.TryGetProperty("string", out var text))
        {
            value = SandboxValue.FromString(ReadStringValue(text, "string"));
            literalName = "string";
            return true;
        }

        if (element.TryGetProperty("opaqueId", out var opaqueId))
        {
            value = ReadOpaqueId(opaqueId);
            literalName = "opaqueId";
            return true;
        }

        if (element.TryGetProperty("path", out var path))
        {
            value = SandboxValue.FromPath(ReadPathValue(path, "path"));
            literalName = "path";
            return true;
        }

        if (element.TryGetProperty("uri", out var uri))
        {
            value = SandboxValue.FromUri(ReadUriValue(uri, "uri"));
            literalName = "uri";
            return true;
        }

        value = SandboxValue.Unit;
        literalName = "";
        return false;
    }

    private static SandboxValue ReadOpaqueId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Error("E-JSON-TYPE", "'opaqueId' must be an object with 'type' and 'value'");
        }

        string? typeName = null;
        string? idValue = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    typeName = ReadStringValue(property.Value, "opaqueId.type");
                    break;
                case "value":
                    idValue = ReadStringValue(property.Value, "opaqueId.value");
                    break;
                default:
                    throw Error("E-JSON-ID", $"'opaqueId' has an unexpected property '{property.Name}'");
            }
        }

        if (typeName is null || idValue is null)
        {
            throw Error("E-JSON-ID", "'opaqueId' must declare 'type' and 'value'");
        }

        if (!SandboxType.IsWellFormedOpaqueIdName(typeName))
        {
            throw Error("E-JSON-ID", "'opaqueId.type' must be a well-formed opaque-id brand");
        }

        if (!SandboxLiteralConstraints.IsOpaqueId(idValue))
        {
            throw Error("E-JSON-ID", "'opaqueId.value' must be a safe opaque-id value");
        }

        return SandboxValue.FromOpaqueId(typeName, idValue);
    }
}
