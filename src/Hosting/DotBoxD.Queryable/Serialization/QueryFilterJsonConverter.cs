using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Serializes the <see cref="QueryFilter"/> tagged union into a compact, host-readable document where each
/// node carries only the members relevant to its <see cref="QueryFilterKind"/>: comparisons emit
/// <c>path</c>/<c>op</c>/<c>value</c>, set membership emits <c>path</c>/<c>values</c>, and the boolean
/// connectives emit <c>terms</c>/<c>term</c>.
/// </summary>
public sealed class QueryFilterJsonConverter : JsonConverter<QueryFilter>
{
    /// <inheritdoc />
    public override QueryFilter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadElement(document.RootElement, options);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, QueryFilter value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        QueryFilterInvariants.RequireValidShape(value);

        writer.WriteStartObject();
        writer.WritePropertyName("kind");
        JsonSerializer.Serialize(writer, value.Kind, options);

        switch (value.Kind)
        {
            case QueryFilterKind.Compare:
                writer.WriteString("path", value.Field);
                writer.WritePropertyName("op");
                JsonSerializer.Serialize(writer, value.Operator, options);
                writer.WritePropertyName("value");
                JsonSerializer.Serialize(writer, QueryFilterInvariants.CompareValue(value), options);
                WriteIgnoreCase(writer, value);
                break;
            case QueryFilterKind.In:
                writer.WriteString("path", value.Field);
                writer.WritePropertyName("values");
                JsonSerializer.Serialize(writer, value.Values, options);
                WriteIgnoreCase(writer, value);
                break;
            case QueryFilterKind.And:
            case QueryFilterKind.Or:
                writer.WritePropertyName("terms");
                JsonSerializer.Serialize(writer, value.Children, options);
                break;
            case QueryFilterKind.Not:
                writer.WritePropertyName("term");
                JsonSerializer.Serialize(writer, value.Children[0], options);
                break;
            case QueryFilterKind.MatchAll:
                break;
            default:
                throw new JsonException($"Unsupported query filter kind '{value.Kind}'.");
        }

        writer.WriteEndObject();
    }

    private static void WriteIgnoreCase(Utf8JsonWriter writer, QueryFilter value)
    {
        if (value.IgnoreCase)
        {
            writer.WriteBoolean("ignoreCase", true);
        }
    }

    private static QueryFilter ReadElement(JsonElement element, JsonSerializerOptions options)
    {
        var kind = Deserialize<QueryFilterKind>(element, "kind", options);
        var ignoreCase = element.TryGetProperty("ignoreCase", out var ic) && ic.GetBoolean();
        return kind switch
        {
            QueryFilterKind.MatchAll => QueryFilter.MatchAll,
            QueryFilterKind.Compare => QueryFilter.Compare(
                RequiredString(element, "path"),
                Deserialize<QueryComparisonOperator>(element, "op", options),
                ReadValue(element, "value", options),
                ignoreCase),
            QueryFilterKind.In => QueryFilter.In(
                RequiredString(element, "path"),
                ReadValues(element, options),
                ignoreCase),
            QueryFilterKind.And => QueryFilter.And(ReadTerms(element, options)),
            QueryFilterKind.Or => QueryFilter.Or(ReadTerms(element, options)),
            QueryFilterKind.Not => QueryFilter.Not(ReadElement(RequiredProperty(element, "term"), options)),
            _ => throw new JsonException($"Unsupported query filter kind '{kind}'."),
        };
    }

    private static IReadOnlyList<QueryFilter> ReadTerms(JsonElement element, JsonSerializerOptions options)
    {
        var terms = new List<QueryFilter>();
        foreach (var child in RequiredProperty(element, "terms").EnumerateArray())
        {
            terms.Add(ReadElement(child, options));
        }

        return terms;
    }

    private static IReadOnlyList<QueryValue> ReadValues(JsonElement element, JsonSerializerOptions options)
    {
        var values = new List<QueryValue>();
        foreach (var child in RequiredProperty(element, "values").EnumerateArray())
        {
            values.Add(child.Deserialize<QueryValue>(options) ?? QueryValue.Null);
        }

        return values;
    }

    private static QueryValue ReadValue(JsonElement element, string property, JsonSerializerOptions options)
        => RequiredProperty(element, property).Deserialize<QueryValue>(options) ?? QueryValue.Null;

    private static T Deserialize<T>(JsonElement element, string property, JsonSerializerOptions options)
        => RequiredProperty(element, property).Deserialize<T>(options)
            ?? throw new JsonException($"Query filter property '{property}' could not be read.");

    private static string RequiredString(JsonElement element, string property)
        => RequiredProperty(element, property).GetString()
            ?? throw new JsonException($"Query filter property '{property}' must be a string.");

    private static JsonElement RequiredProperty(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
            ? value
            : throw new JsonException($"Query filter is missing required property '{property}'.");
}
