using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Serializes a <see cref="QueryValue"/>. The five original kinds (null/bool/integer/number/string) are written
/// as raw JSON scalars (<c>"player-1"</c>, <c>5</c>, <c>true</c>, <c>null</c>) and read back by token type, so
/// their wire form — and therefore every existing fingerprint — is unchanged. The exact kinds added later
/// (Guid, Decimal, UnsignedInteger, Timestamp) cannot ride a bare scalar without colliding (a Guid/Timestamp
/// looks like a String; a Decimal/UnsignedInteger like a Number/Integer and would lose scale/range), so they
/// use a tagged object <c>{"kind":"…","value":"…"}</c> with the value as a canonical string. Capture provenance
/// (<see cref="QueryValue.ParameterKey"/>) is runtime-only and is not part of the wire form.
/// </summary>
public sealed class QueryValueJsonConverter : JsonConverter<QueryValue>
{
    /// <inheritdoc />
    public override QueryValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => QueryValue.Null,
            JsonTokenType.True => QueryValue.FromBoolean(true),
            JsonTokenType.False => QueryValue.FromBoolean(false),
            JsonTokenType.String => QueryValue.FromString(reader.GetString()),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.StartObject => ReadTagged(ref reader),
            _ => throw new JsonException($"Unsupported JSON token '{reader.TokenType}' for a query value."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, QueryValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        switch (value.Kind)
        {
            case QueryValueKind.Null:
                writer.WriteNullValue();
                break;
            case QueryValueKind.Boolean:
                writer.WriteBooleanValue(value.Boolean);
                break;
            case QueryValueKind.Integer:
                writer.WriteNumberValue(value.Integer);
                break;
            case QueryValueKind.Number:
                writer.WriteNumberValue(value.Number);
                break;
            case QueryValueKind.String:
                writer.WriteStringValue(value.String);
                break;
            case QueryValueKind.Guid:
                WriteTagged(writer, "guid", value.Guid.ToString("D"));
                break;
            case QueryValueKind.Decimal:
                WriteTagged(writer, "decimal", QueryValue.CanonicalDecimal(value.Decimal));
                break;
            case QueryValueKind.UnsignedInteger:
                WriteTagged(writer, "ulong", value.UnsignedInteger.ToString(CultureInfo.InvariantCulture));
                break;
            case QueryValueKind.Timestamp:
                WriteTagged(writer, "timestamp", QueryValue.CanonicalTimestamp(value.Timestamp));
                break;
            default:
                throw new JsonException($"Unsupported query value kind '{value.Kind}'.");
        }
    }

    private static void WriteTagged(Utf8JsonWriter writer, string kind, string value)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        writer.WriteString("value", value);
        writer.WriteEndObject();
    }

    private static QueryValue ReadNumber(ref Utf8JsonReader reader) =>
        reader.TryGetInt64(out var integer)
            ? QueryValue.FromInteger(integer)
            : QueryValue.FromNumber(reader.GetDouble());

    // A value position never holds a filter/projection object (those have their own converters), so a
    // StartObject here is unambiguously a tagged exact-kind value.
    private static QueryValue ReadTagged(ref Utf8JsonReader reader)
    {
        string? kind = null;
        string? text = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var property = reader.GetString();
            reader.Read();
            if (property == "kind")
            {
                kind = reader.GetString();
            }
            else if (property == "value")
            {
                text = reader.GetString();
            }
        }

        if (text is null)
        {
            throw new JsonException("A tagged query value is missing its 'value'.");
        }

        return kind switch
        {
            "guid" => ReadGuid(kind, text),
            "decimal" => ReadDecimal(kind, text),
            "ulong" => ReadUnsignedInteger(kind, text),
            "timestamp" => ReadTimestamp(kind, text),
            _ => throw new JsonException($"Unknown tagged query value kind '{kind}'."),
        };
    }

    private static QueryValue ReadGuid(string kind, string text) =>
        Guid.TryParse(text, out var value)
            ? QueryValue.FromGuid(value)
            : throw InvalidTaggedValue(kind, text);

    private static QueryValue ReadDecimal(string kind, string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? QueryValue.FromDecimal(value)
            : throw InvalidTaggedValue(kind, text);

    private static QueryValue ReadUnsignedInteger(string kind, string text) =>
        ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? QueryValue.FromUnsignedInteger(value)
            : throw InvalidTaggedValue(kind, text);

    private static QueryValue ReadTimestamp(string kind, string text)
    {
        if (!QueryValue.HasExplicitTimestampOffset(text))
        {
            throw new JsonException($"Timestamp '{text}' must include an explicit UTC 'Z' or +/-hh:mm offset.");
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? QueryValue.FromTimestamp(value)
            : throw InvalidTaggedValue(kind, text);
    }

    private static JsonException InvalidTaggedValue(string kind, string text) =>
        new($"Invalid tagged query value '{text}' for kind '{kind}'.");
}
