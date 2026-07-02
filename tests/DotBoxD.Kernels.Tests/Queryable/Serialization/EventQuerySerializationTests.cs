using System.Text.Json;
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
}
