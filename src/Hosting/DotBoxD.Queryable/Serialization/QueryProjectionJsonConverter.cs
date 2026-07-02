using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Serialization;

/// <summary>
/// Serializes the <see cref="QueryProjection"/> shape into a compact document: identity emits only its
/// kind, a member projection emits <c>path</c>, and a construct projection emits <c>type</c> plus a
/// <c>fields</c> array of <c>{name, path}</c> (member read) or <c>{name, value}</c> (constant).
/// </summary>
public sealed class QueryProjectionJsonConverter : JsonConverter<QueryProjection>
{
    /// <inheritdoc />
    public override QueryProjection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var document = JsonDocument.ParseValue(ref reader);
        var element = document.RootElement;
        var kind = Deserialize<QueryProjectionKind>(element, "kind", options);
        RejectInactiveArmProperties(element, kind);
        return kind switch
        {
            QueryProjectionKind.Identity => QueryProjection.Identity,
            QueryProjectionKind.Member => QueryProjection.Member(RequiredString(element, "path")),
            QueryProjectionKind.Construct => QueryProjection.Construct(
                RequiredString(element, "type"),
                ReadFields(element, options)),
            _ => throw new JsonException($"Unsupported query projection kind '{kind}'."),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, QueryProjection value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        QueryProjectionInvariants.RequireValidShape(value);

        writer.WriteStartObject();
        writer.WritePropertyName("kind");
        JsonSerializer.Serialize(writer, value.Kind, options);

        switch (value.Kind)
        {
            case QueryProjectionKind.Identity:
                break;
            case QueryProjectionKind.Member:
                writer.WriteString("path", QueryProjectionInvariants.MemberPath(value));
                break;
            case QueryProjectionKind.Construct:
                writer.WriteString("type", QueryProjectionInvariants.ConstructTypeName(value));
                writer.WritePropertyName("fields");
                WriteFields(writer, value.Fields, options);
                break;
            default:
                throw new JsonException($"Unsupported query projection kind '{value.Kind}'.");
        }

        writer.WriteEndObject();
    }

    private static void WriteFields(
        Utf8JsonWriter writer,
        IReadOnlyList<QueryProjectionField> fields,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var field in fields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", QueryProjectionInvariants.FieldName(field));
            if (QueryProjectionInvariants.FieldHasPath(field))
            {
                writer.WriteString("path", QueryProjectionInvariants.FieldPath(field));
            }
            else
            {
                writer.WritePropertyName("value");
                JsonSerializer.Serialize(writer, QueryProjectionInvariants.FieldConstant(field), options);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static IReadOnlyList<QueryProjectionField> ReadFields(JsonElement element, JsonSerializerOptions options)
    {
        var fields = new List<QueryProjectionField>();
        foreach (var field in RequiredProperty(element, "fields").EnumerateArray())
        {
            var name = field.TryGetProperty("name", out var n) && n.GetString() is { } parsed
                ? parsed
                : throw new JsonException("Query projection field is missing 'name'.");

            // A field is either a member read ('path') or a constant ('value'), never both or neither —
            // otherwise the projection shape is ambiguous and must be rejected rather than silently coerced.
            var hasPath = field.TryGetProperty("path", out var path);
            var hasValue = field.TryGetProperty("value", out var v);
            if (hasPath == hasValue)
            {
                throw new JsonException(
                    $"Query projection field '{name}' must contain exactly one of 'path' or 'value'.");
            }

            if (hasPath)
            {
                fields.Add(QueryProjectionField.FromMember(
                    name,
                    path.GetString() ?? throw new JsonException(
                        $"Query projection field '{name}' property 'path' must be a string.")));
            }
            else
            {
                fields.Add(QueryProjectionField.FromConstant(name, v.Deserialize<QueryValue>(options) ?? QueryValue.Null));
            }
        }

        return fields;
    }

    private static void RejectInactiveArmProperties(JsonElement element, QueryProjectionKind kind)
    {
        switch (kind)
        {
            case QueryProjectionKind.Identity:
                RejectInactiveArmProperty(element, kind, "path");
                RejectInactiveArmProperty(element, kind, "type");
                RejectInactiveArmProperty(element, kind, "fields");
                break;
            case QueryProjectionKind.Member:
                RejectInactiveArmProperty(element, kind, "type");
                RejectInactiveArmProperty(element, kind, "fields");
                break;
            case QueryProjectionKind.Construct:
                RejectInactiveArmProperty(element, kind, "path");
                break;
        }
    }

    private static void RejectInactiveArmProperty(JsonElement element, QueryProjectionKind kind, string property)
    {
        if (element.TryGetProperty(property, out _))
        {
            throw new JsonException(
                $"QueryProjection {kind} JSON cannot carry inactive union-arm property '{property}'.");
        }
    }

    private static T Deserialize<T>(JsonElement element, string property, JsonSerializerOptions options)
        => RequiredProperty(element, property).Deserialize<T>(options)
            ?? throw new JsonException($"Query projection property '{property}' could not be read.");

    private static string RequiredString(JsonElement element, string property)
        => RequiredProperty(element, property).GetString()
            ?? throw new JsonException($"Query projection property '{property}' must be a string.");

    private static JsonElement RequiredProperty(JsonElement element, string property)
        => element.TryGetProperty(property, out var value)
            ? value
            : throw new JsonException($"Query projection is missing required property '{property}'.");
}
