using System.Text.Json;

namespace DotBoxd.Kernels;

internal static class JsonImport
{
    public static readonly SourceSpan JsonSpan = new(0, 0);

    public static JsonElement Required(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            ? value
            : throw Error("E-JSON-MISSING", $"missing required property '{name}'");

    public static JsonElement RequiredArray(JsonElement element, string name)
    {
        var value = Required(element, name);
        RequireArray(value, name);
        return value;
    }

    public static string RequiredString(JsonElement element, string name)
        => ReadStringValue(Required(element, name), name);

    public static string? OptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) ? ReadStringValue(value, name) : null;

    public static string ReadStringValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String) {
            throw Error("E-JSON-TYPE", $"'{name}' must be a string");
        }

        return value.GetString() ?? "";
    }

    public static string ReadPathValue(JsonElement value, string name)
    {
        var path = ReadStringValue(value, name);
        if (!SandboxLiteralConstraints.IsPortableRelativePath(path)) {
            throw Error("E-JSON-PATH", $"'{name}' must be a portable relative path");
        }

        return path;
    }

    public static string ReadUriValue(JsonElement value, string name)
    {
        var uri = ReadStringValue(value, name);
        if (!SandboxLiteralConstraints.IsSandboxUri(uri)) {
            throw Error("E-JSON-URI", $"'{name}' must be an absolute URI without user info");
        }

        return uri;
    }

    public static int ReadInt32Value(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result)) {
            throw Error("E-JSON-TYPE", $"'{name}' must be a 32-bit integer");
        }

        return result;
    }

    public static long ReadInt64Value(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result)) {
            throw Error("E-JSON-TYPE", $"'{name}' must be a 64-bit integer");
        }

        return result;
    }

    public static double ReadDoubleValue(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result)) {
            throw Error("E-JSON-TYPE", $"'{name}' must be a finite number");
        }

        return result;
    }

    public static bool ReadBoolValue(JsonElement value, string name)
    {
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) {
            throw Error("E-JSON-TYPE", $"'{name}' must be a boolean");
        }

        return value.GetBoolean();
    }

    public static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object) {
            throw Error("E-JSON-TYPE", $"{name} must be an object");
        }
    }

    public static JsonElement RequireArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array) {
            throw Error("E-JSON-TYPE", $"{name} must be an array");
        }

        return value;
    }

    public static T[] AllocateArray<T>(JsonElement array, out int count)
    {
        count = array.GetArrayLength();
        return count == 0 ? Array.Empty<T>() : new T[count];
    }

    public static void RequireAllowedProperties(JsonElement value, string name, params string[] allowed)
    {
        RequireUniqueProperties(value, name);
        foreach (var property in value.EnumerateObject()) {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal)) {
                throw Error("E-JSON-SCHEMA", $"{name} contains unsupported property '{property.Name}'");
            }
        }
    }

    public static void RequireUniqueProperties(JsonElement value, string name)
    {
        RequireObject(value, name);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) {
            if (!seen.Add(property.Name)) {
                throw Error("E-JSON-SCHEMA", $"{name} contains duplicate property '{property.Name}'");
            }
        }
    }

    public static SandboxValidationException Error(string code, string message)
        => new([new SandboxDiagnostic(code, message, Span: JsonSpan)]);
}
