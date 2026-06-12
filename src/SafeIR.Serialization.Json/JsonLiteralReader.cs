namespace SafeIR;

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

        if (TryReadOpaqueId(element, "playerId", "PlayerId", SandboxValue.FromPlayerId, out value, out literalName) ||
            TryReadOpaqueId(element, "itemId", "ItemId", SandboxValue.FromItemId, out value, out literalName) ||
            TryReadOpaqueId(element, "questId", "QuestId", SandboxValue.FromQuestId, out value, out literalName) ||
            TryReadOpaqueId(element, "mapId", "MapId", SandboxValue.FromMapId, out value, out literalName))
        {
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

    private static bool TryReadOpaqueId(
        JsonElement element,
        string literalKey,
        string typeName,
        Func<string, SandboxValue> create,
        out SandboxValue value,
        out string literalName)
    {
        if (!element.TryGetProperty(literalKey, out var id))
        {
            value = SandboxValue.Unit;
            literalName = "";
            return false;
        }

        var text = ReadStringValue(id, literalKey);
        if (!SandboxLiteralConstraints.IsOpaqueId(text))
        {
            throw Error("E-JSON-ID", $"'{literalKey}' must be a safe {typeName} value");
        }

        value = create(text);
        literalName = literalKey;
        return true;
    }
}
