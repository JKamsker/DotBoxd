using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Serialization.Json.Internal;

using System.Text.Json;
using static JsonImport;

internal static class JsonLiteralReader
{
    public static bool TryRead(
        JsonElement element,
        JsonSourceMap source,
        out SandboxValue value,
        out string literalName)
    {
        if (element.TryGetProperty("unit", out var unit))
        {
            if (!ReadBoolValue(unit, "unit", source))
            {
                throw Error("E-JSON-TYPE", "'unit' must be true", source.SpanFor(unit));
            }

            value = SandboxValue.Unit;
            literalName = "unit";
            return true;
        }

        if (element.TryGetProperty("i32", out var i32))
        {
            value = SandboxValue.FromInt32(ReadInt32Value(i32, "i32", source));
            literalName = "i32";
            return true;
        }

        if (element.TryGetProperty("i64", out var i64))
        {
            value = SandboxValue.FromInt64(ReadInt64Value(i64, "i64", source));
            literalName = "i64";
            return true;
        }

        if (element.TryGetProperty("f64", out var f64))
        {
            value = SandboxValue.FromDouble(ReadDoubleValue(f64, "f64", source));
            literalName = "f64";
            return true;
        }

        if (element.TryGetProperty("bool", out var boolean))
        {
            value = SandboxValue.FromBool(ReadBoolValue(boolean, "bool", source));
            literalName = "bool";
            return true;
        }

        if (element.TryGetProperty("string", out var text))
        {
            value = SandboxValue.FromString(ReadStringValue(text, "string", source));
            literalName = "string";
            return true;
        }

        if (element.TryGetProperty("guid", out var guid))
        {
            value = SandboxValue.FromGuid(ReadGuidValue(guid, "guid", source));
            literalName = "guid";
            return true;
        }

        if (element.TryGetProperty("opaqueId", out var opaqueId))
        {
            value = ReadOpaqueId(opaqueId, source);
            literalName = "opaqueId";
            return true;
        }

        if (element.TryGetProperty("path", out var path))
        {
            value = SandboxValue.FromPath(ReadPathValue(path, "path", source));
            literalName = "path";
            return true;
        }

        if (element.TryGetProperty("uri", out var uri))
        {
            value = SandboxValue.FromUri(ReadUriValue(uri, "uri", source));
            literalName = "uri";
            return true;
        }

        value = SandboxValue.Unit;
        literalName = "";
        return false;
    }

    private static SandboxValue ReadOpaqueId(JsonElement element, JsonSourceMap source)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Error(
                "E-JSON-TYPE",
                "'opaqueId' must be an object with 'type' and 'value'",
                source.SpanFor(element));
        }

        RequireAllowedProperties(element, "opaqueId", ["type", "value"]);
        string? typeName = null;
        string? idValue = null;
        JsonElement? typeElement = null;
        JsonElement? valueElement = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type":
                    typeElement = property.Value;
                    typeName = ReadStringValue(property.Value, "opaqueId.type", source);
                    break;
                case "value":
                    valueElement = property.Value;
                    idValue = ReadStringValue(property.Value, "opaqueId.value", source);
                    break;
            }
        }

        if (typeName is null || idValue is null)
        {
            throw Error("E-JSON-ID", "'opaqueId' must declare 'type' and 'value'", source.SpanFor(element));
        }

        if (!SandboxType.IsWellFormedOpaqueIdName(typeName))
        {
            throw Error("E-JSON-ID", "'opaqueId.type' must be a well-formed opaque-id brand", source.SpanFor(typeElement!.Value));
        }

        if (!SandboxLiteralConstraints.IsOpaqueId(idValue))
        {
            throw Error("E-JSON-ID", "'opaqueId.value' must be a safe opaque-id value", source.SpanFor(valueElement!.Value));
        }

        return SandboxValue.FromOpaqueId(typeName, idValue);
    }
}
