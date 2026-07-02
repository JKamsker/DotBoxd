using System.Text.Json;
using DotBoxD.Kernels.Model;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQuerySerializationTests
{
    private static EventQueryDocument SampleDocument()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.AttackerId == "player-1" && e.Damage >= 5);
        var projection = ExpressionQueryTranslator.TranslateProjection<AttackTestEvent, AttackNotice>(
            e => new AttackNotice(e.AttackerId, e.TargetId, e.Damage));
        return EventQueryDocument.Create(ExpressionQueryTranslator.EventName<AttackTestEvent>(), filter, projection);
    }

    [Fact]
    public void Document_round_trips_through_serialization()
    {
        var document = SampleDocument();

        var json = EventQueryJson.Serialize(document);
        var restored = EventQueryJson.Deserialize(json);
        var reserialized = EventQueryJson.Serialize(restored);

        Assert.Equal(json, reserialized);
        Assert.Equal("DotBoxD.Kernels.Tests.Queryable.AttackTestEvent", restored.EventName);
        Assert.Equal(QueryFilterKind.And, restored.Filter.Kind);
        Assert.Equal(QueryProjectionKind.Construct, restored.Projection.Kind);
    }

    [Fact]
    public void Missing_filter_and_projection_default_to_match_all_identity()
    {
        var restored = EventQueryJson.Deserialize("{\"event\":\"E\"}");

        Assert.Equal("E", restored.EventName);
        Assert.Equal(QueryFilterKind.MatchAll, restored.Filter.Kind);
        Assert.Equal(QueryProjectionKind.Identity, restored.Projection.Kind);
        Assert.Equal(64, QueryFingerprint.Compute(restored).Length);
    }

    [Theory]
    [InlineData("{\"event\":\"E\",\"filter\":null}", "filter")]
    [InlineData("{\"event\":\"E\",\"projection\":null}", "projection")]
    [InlineData("{\"event\":\"E\",\"filter\":null,\"projection\":null}", "filter")]
    public void Explicit_null_document_subtrees_are_rejected(string json, string property)
    {
        var exception = Record.Exception(() => EventQueryJson.Deserialize(json));

        Assert.NotNull(exception);
        Assert.True(exception is JsonException or InvalidOperationException);
        Assert.Contains("EventQueryDocument", exception.Message, StringComparison.Ordinal);
        Assert.Contains(property, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Json_uses_compact_host_readable_tokens()
    {
        var json = EventQueryJson.Serialize(SampleDocument());

        Assert.Contains("\"kind\":\"compare\"", json, StringComparison.Ordinal);
        Assert.Contains("\"op\":\"eq\"", json, StringComparison.Ordinal);
        Assert.Contains("\"op\":\"gte\"", json, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"Damage\"", json, StringComparison.Ordinal);
        Assert.Contains("\"value\":5", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"construct\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Structural_strings_round_trip_valid_surrogate_pairs()
    {
        var value = "prefix-\ud83d\ude00-suffix";
        var document = EventQueryDocument.Create(
            value,
            QueryFilter.MatchAll,
            QueryProjection.Construct(
                "Notice",
                [QueryProjectionField.FromMember(value, value)]));

        var restored = EventQueryJson.Deserialize(EventQueryJson.Serialize(document));

        Assert.Equal(value, restored.EventName);
        var field = Assert.Single(restored.Projection.Fields);
        Assert.Equal(value, field.Name);
        Assert.Equal(value, field.Path);
    }

    [Theory]
    [InlineData(StructuralStringTarget.EventName)]
    [InlineData(StructuralStringTarget.ProjectionPath)]
    [InlineData(StructuralStringTarget.ProjectionFieldName)]
    public void Structural_strings_round_trip_without_utf16_replacement(
        StructuralStringTarget target)
    {
        const string expected = "prefix-\ud800-suffix";
        var exception = Record.Exception(() =>
        {
            var restored = EventQueryJson.Deserialize(EventQueryJson.Serialize(CreateDocument(target, expected)));
            Assert.Equal(expected, ReadRestoredValue(target, restored));
        });

        if (exception is null)
        {
            return;
        }

        Assert.True(
            exception is JsonException or InvalidOperationException or SandboxValidationException,
            $"Expected JSON boundary rejection or exact round trip, got {exception.GetType().Name}: {exception.Message}");
        AssertMalformedUtf16Message(exception);
    }

    [Fact]
    public void Fingerprint_is_stable_and_order_independent()
    {
        var attacker = QueryFilter.Compare("AttackerId", QueryComparisonOperator.Equal, QueryValue.FromString("player-1"));
        var damage = QueryFilter.Compare("Damage", QueryComparisonOperator.GreaterThanOrEqual, QueryValue.FromInteger(5));

        var forward = EventQueryDocument.Create("E", QueryFilter.And([attacker, damage]), QueryProjection.Identity);
        var reversed = EventQueryDocument.Create("E", QueryFilter.And([damage, attacker]), QueryProjection.Identity);

        Assert.Equal(QueryFingerprint.Compute(forward), QueryFingerprint.Compute(reversed));
        Assert.Equal(64, QueryFingerprint.Compute(forward).Length);
    }

    [Fact]
    public void Fingerprint_is_independent_of_conjunction_grouping()
    {
        var grouped = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.AttackerId == "p" && (e.TargetId == "t" && e.Damage >= 5));
        var flat = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => (e.AttackerId == "p" && e.TargetId == "t") && e.Damage >= 5);

        var docGrouped = EventQueryDocument.Create("E", grouped, QueryProjection.Identity);
        var docFlat = EventQueryDocument.Create("E", flat, QueryProjection.Identity);

        Assert.Equal(QueryFingerprint.Compute(docGrouped), QueryFingerprint.Compute(docFlat));
    }

    [Fact]
    public void Fingerprint_differs_for_different_values()
    {
        var a = EventQueryDocument.Create(
            "E",
            QueryFilter.Compare("AttackerId", QueryComparisonOperator.Equal, QueryValue.FromString("player-1")),
            QueryProjection.Identity);
        var b = EventQueryDocument.Create(
            "E",
            QueryFilter.Compare("AttackerId", QueryComparisonOperator.Equal, QueryValue.FromString("player-2")),
            QueryProjection.Identity);

        Assert.NotEqual(QueryFingerprint.Compute(a), QueryFingerprint.Compute(b));
    }

    private static void AssertMalformedUtf16Message(Exception exception)
    {
        Assert.True(
            exception.Message.Contains("surrogate", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("UTF-16", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase),
            $"Expected malformed UTF-16 boundary message, got: {exception.Message}");
    }

    private static EventQueryDocument CreateDocument(StructuralStringTarget target, string value)
        => target switch
        {
            StructuralStringTarget.EventName =>
                EventQueryDocument.Create(value, QueryFilter.MatchAll, QueryProjection.Identity),
            StructuralStringTarget.ProjectionPath =>
                EventQueryDocument.Create("AttackEvent", QueryFilter.MatchAll, QueryProjection.Member(value)),
            StructuralStringTarget.ProjectionFieldName =>
                EventQueryDocument.Create(
                    "AttackEvent",
                    QueryFilter.MatchAll,
                    QueryProjection.Construct(
                        "Notice",
                        [QueryProjectionField.FromMember(value, "AttackerId")])),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    private static string ReadRestoredValue(StructuralStringTarget target, EventQueryDocument document)
        => target switch
        {
            StructuralStringTarget.EventName => document.EventName,
            StructuralStringTarget.ProjectionPath => document.Projection.Path!,
            StructuralStringTarget.ProjectionFieldName => Assert.Single(document.Projection.Fields).Name,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    public enum StructuralStringTarget
    {
        EventName,
        ProjectionPath,
        ProjectionFieldName,
    }
}
