using System.Text.Json;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Serialization.Json;

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
        => ReadStringValue(value, name, JsonSpan);

    public static string ReadStringValue(JsonElement value, string name, SourceSpan span)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a string", span);
        }

        return value.GetString() ?? "";
    }

    public static string ReadStringValue(JsonElement value, string name, JsonSourceMap source)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a string", source.SpanFor(value));
        }

        return value.GetString() ?? "";
    }

    public static string ReadPathValue(JsonElement value, string name)
        => ReadPathValue(value, name, JsonSpan);

    public static string ReadPathValue(JsonElement value, string name, SourceSpan span)
    {
        var path = ReadStringValue(value, name, span);
        if (!SandboxLiteralConstraints.IsPortableRelativePath(path))
        {
            throw Error("E-JSON-PATH", $"'{name}' must be a portable relative path", span);
        }

        return path;
    }

    public static string ReadPathValue(JsonElement value, string name, JsonSourceMap source)
    {
        var path = ReadStringValue(value, name, source);
        if (!SandboxLiteralConstraints.IsPortableRelativePath(path))
        {
            throw Error("E-JSON-PATH", $"'{name}' must be a portable relative path", source.SpanFor(value));
        }

        return path;
    }

    public static string ReadUriValue(JsonElement value, string name)
        => ReadUriValue(value, name, JsonSpan);

    public static string ReadUriValue(JsonElement value, string name, SourceSpan span)
    {
        var uri = ReadStringValue(value, name, span);
        if (!SandboxLiteralConstraints.IsSandboxUri(uri))
        {
            throw Error("E-JSON-URI", $"'{name}' must be an absolute URI without user info", span);
        }

        return uri;
    }

    public static string ReadUriValue(JsonElement value, string name, JsonSourceMap source)
    {
        var uri = ReadStringValue(value, name, source);
        if (!SandboxLiteralConstraints.IsSandboxUri(uri))
        {
            throw Error("E-JSON-URI", $"'{name}' must be an absolute URI without user info", source.SpanFor(value));
        }

        return uri;
    }

    public static System.Guid ReadGuidValue(JsonElement value, string name)
        => ReadGuidValue(value, name, JsonSpan);

    public static System.Guid ReadGuidValue(JsonElement value, string name, SourceSpan span)
    {
        var text = ReadStringValue(value, name, span);
        // Pin the canonical hyphenated form JsonExporter emits (Guid.ToString() == "D"), so a round-trip is
        // deterministic and non-canonical spellings (braces, no hyphens) are rejected up front.
        if (!System.Guid.TryParseExact(text, "D", out var guid))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a canonical hyphenated GUID", span);
        }

        return guid;
    }

    public static System.Guid ReadGuidValue(JsonElement value, string name, JsonSourceMap source)
    {
        var text = ReadStringValue(value, name, source);
        if (!System.Guid.TryParseExact(text, "D", out var guid))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a canonical hyphenated GUID", source.SpanFor(value));
        }

        return guid;
    }

    public static int ReadInt32Value(JsonElement value, string name)
        => ReadInt32Value(value, name, JsonSpan);

    public static int ReadInt32Value(JsonElement value, string name, SourceSpan span)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a 32-bit integer", span);
        }

        return result;
    }

    public static int ReadInt32Value(JsonElement value, string name, JsonSourceMap source)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a 32-bit integer", source.SpanFor(value));
        }

        return result;
    }

    public static long ReadInt64Value(JsonElement value, string name)
        => ReadInt64Value(value, name, JsonSpan);

    public static long ReadInt64Value(JsonElement value, string name, SourceSpan span)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a 64-bit integer", span);
        }

        return result;
    }

    public static long ReadInt64Value(JsonElement value, string name, JsonSourceMap source)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a 64-bit integer", source.SpanFor(value));
        }

        return result;
    }

    public static double ReadDoubleValue(JsonElement value, string name)
        => ReadDoubleValue(value, name, JsonSpan);

    public static double ReadDoubleValue(JsonElement value, string name, SourceSpan span)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a finite number", span);
        }

        return result;
    }

    public static double ReadDoubleValue(JsonElement value, string name, JsonSourceMap source)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a finite number", source.SpanFor(value));
        }

        return result;
    }

    public static bool ReadBoolValue(JsonElement value, string name)
        => ReadBoolValue(value, name, JsonSpan);

    public static bool ReadBoolValue(JsonElement value, string name, SourceSpan span)
    {
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a boolean", span);
        }

        return value.GetBoolean();
    }

    public static bool ReadBoolValue(JsonElement value, string name, JsonSourceMap source)
    {
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw Error("E-JSON-TYPE", $"'{name}' must be a boolean", source.SpanFor(value));
        }

        return value.GetBoolean();
    }

    public static void RequireObject(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Error("E-JSON-TYPE", $"{name} must be an object");
        }
    }

    public static JsonElement RequireArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
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
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw Error("E-JSON-SCHEMA", $"{name} contains unsupported property '{property.Name}'");
            }
        }
    }

    public static void RequireUniqueProperties(JsonElement value, string name)
    {
        RequireObject(value, name);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Error("E-JSON-SCHEMA", $"{name} contains duplicate property '{property.Name}'");
            }
        }
    }

    public static SandboxValidationException Error(string code, string message)
        => Error(code, message, JsonSpan);

    public static SandboxValidationException Error(string code, string message, SourceSpan span)
        => new([new SandboxDiagnostic(code, message, Span: span)]);
}
