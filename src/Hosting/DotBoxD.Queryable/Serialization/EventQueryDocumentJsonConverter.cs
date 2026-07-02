using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Serializes an <see cref="EventQueryDocument"/> as a versioned envelope
/// (<c>{ "version", "event", "filter", "projection" }</c>). The version is written for forward
/// compatibility and tolerated (any value, or absent) on read so older logs and caches stay loadable.
/// </summary>
public sealed class EventQueryDocumentJsonConverter : JsonConverter<EventQueryDocument>
{
    private const int CurrentVersion = 1;

    /// <inheritdoc />
    public override EventQueryDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        var eventName = root.TryGetProperty("event", out var e) && e.GetString() is { } name
            ? EventQueryJsonStringSafety.RequireWellFormedUtf16(name, "event")
            : throw new JsonException("Event query document is missing required property 'event'.");

        var filter = root.TryGetProperty("filter", out var f)
            ? ReadFilter(f, options)
            : QueryFilter.MatchAll;

        var projection = root.TryGetProperty("projection", out var p)
            ? ReadProjection(p, options)
            : QueryProjection.Identity;

        return EventQueryDocument.Create(eventName, filter, projection);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, EventQueryDocument value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        writer.WriteStartObject();
        writer.WriteNumber("version", CurrentVersion);
        writer.WriteString(
            "event",
            EventQueryJsonStringSafety.RequireWellFormedUtf16(value.EventName, "event"));
        writer.WritePropertyName("filter");
        JsonSerializer.Serialize(writer, value.Filter, options);
        writer.WritePropertyName("projection");
        JsonSerializer.Serialize(writer, value.Projection, options);
        writer.WriteEndObject();
    }

    private static QueryFilter ReadFilter(JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException("EventQueryDocument property 'filter' must not be null.");
        }

        return element.Deserialize<QueryFilter>(options)
            ?? throw new JsonException("EventQueryDocument property 'filter' could not be read.");
    }

    private static QueryProjection ReadProjection(JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            throw new JsonException("EventQueryDocument property 'projection' must not be null.");
        }

        return element.Deserialize<QueryProjection>(options)
            ?? throw new JsonException("EventQueryDocument property 'projection' could not be read.");
    }
}
