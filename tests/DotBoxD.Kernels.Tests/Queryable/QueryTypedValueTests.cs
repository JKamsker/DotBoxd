using System.Text.Json;
using DotBoxD.Abstractions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

/// <summary>
/// Covers the exact typed value kinds (Guid, Decimal, UnsignedInteger, Timestamp) and signed-zero
/// normalization: capture, comparison exactness, JSON + text round-trips, fingerprint canonicalization, and
/// the planner/routing split (new kinds are equality-routable in the dispatcher but host-index-ineligible).
/// </summary>
public sealed class QueryTypedValueTests
{
    private static readonly Guid GuidA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid GuidB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private enum UlongFlags : ulong
    {
        Max = ulong.MaxValue,
    }

    private sealed record GuidEvent(Guid Id);
    private sealed record MoneyEvent(decimal Price);
    private sealed record BigIdEvent(ulong Id);
    private sealed record TimedEvent(DateTimeOffset At);

    private static string Fingerprint(QueryFilter filter) =>
        QueryFingerprint.Compute(EventQueryDocument.Create("E", filter, QueryProjection.Identity));

    private static QueryValue JsonRoundTrip(QueryValue value) =>
        JsonSerializer.Deserialize<QueryValue>(JsonSerializer.Serialize(value, EventQueryJson.Options), EventQueryJson.Options)!;

    // ---- value model / capture ----

    [Fact]
    public void TryFromObject_maps_exact_kinds_without_collapsing_to_double()
    {
        Assert.True(QueryValue.TryFromObject(5UL, out var u));
        Assert.Equal(QueryValueKind.UnsignedInteger, u.Kind);

        Assert.True(QueryValue.TryFromObject(1.5m, out var dec));
        Assert.Equal(QueryValueKind.Decimal, dec.Kind);

        Assert.True(QueryValue.TryFromObject(GuidA, out var g));
        Assert.Equal(QueryValueKind.Guid, g.Kind);

        Assert.True(QueryValue.TryFromObject(new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero), out var ts));
        Assert.Equal(QueryValueKind.Timestamp, ts.Kind);

        Assert.True(QueryValue.TryFromObject(new DateTime(2026, 6, 19, 12, 0, 0, DateTimeKind.Utc), out var dtTs));
        Assert.Equal(QueryValueKind.Timestamp, dtTs.Kind);

        Assert.True(QueryValue.TryFromObject(new DateOnly(2026, 6, 19), out var dateTs));
        Assert.Equal(QueryValueKind.Timestamp, dateTs.Kind);

        Assert.True(QueryValue.TryFromObject(UlongFlags.Max, out var enumU));
        Assert.Equal(QueryValueKind.UnsignedInteger, enumU.Kind);
        Assert.Equal(ulong.MaxValue, enumU.UnsignedInteger);
    }

    [Fact]
    public void Unsigned_max_is_captured_exactly()
    {
        Assert.True(QueryValue.TryFromObject(ulong.MaxValue, out var value));
        Assert.Equal(ulong.MaxValue, value.UnsignedInteger);
    }

    [Fact]
    public void Decimal_equality_is_scale_insensitive()
    {
        Assert.Equal(QueryValue.FromDecimal(1.10m), QueryValue.FromDecimal(1.100m));
        Assert.Equal(QueryValue.FromDecimal(1.10m).GetHashCode(), QueryValue.FromDecimal(1.100m).GetHashCode());
    }

    [Fact]
    public void Negative_zero_normalizes_to_positive_zero()
    {
        Assert.Equal(QueryValue.FromNumber(0.0), QueryValue.FromNumber(-0.0));
        Assert.Equal(QueryValue.FromNumber(0.0).GetHashCode(), QueryValue.FromNumber(-0.0).GetHashCode());
        Assert.Equal(Fingerprint(QueryFilter.Compare("X", QueryComparisonOperator.Equal, QueryValue.FromNumber(0.0))),
            Fingerprint(QueryFilter.Compare("X", QueryComparisonOperator.Equal, QueryValue.FromNumber(-0.0))));
    }

    // ---- comparison exactness ----

    [Fact]
    public void Unsigned_equality_is_exact_above_two_pow_fifty_three()
    {
        var expected = QueryValue.FromUnsignedInteger(9007199254740993UL);
        Assert.True(QueryValueComparer.AreEqual(9007199254740993UL, expected, ignoreCase: false));
        Assert.False(QueryValueComparer.AreEqual(9007199254740992UL, expected, ignoreCase: false));
    }

    [Fact]
    public void Decimal_comparison_is_exact_and_interoperates_with_integers()
    {
        Assert.True(QueryValueComparer.AreEqual(1.005m, QueryValue.FromDecimal(1.005m), ignoreCase: false));
        Assert.True(QueryValueComparer.AreEqual(1.10m, QueryValue.FromDecimal(1.100m), ignoreCase: false));
        Assert.True(QueryValueComparer.AreEqual(1m, QueryValue.FromInteger(1), ignoreCase: false));
        Assert.True(QueryValueComparer.AreEqual(5, QueryValue.FromDecimal(5m), ignoreCase: false));
        Assert.True(QueryValueComparer.Compare(3m, QueryComparisonOperator.GreaterThan, QueryValue.FromDecimal(2m), ignoreCase: false));
    }

    [Fact]
    public void Decimal_falls_back_to_double_only_against_a_floating_member()
    {
        // A double member vs a decimal bound uses the (lossy) double path because a float/double is involved.
        Assert.True(QueryValueComparer.AreEqual(1.5d, QueryValue.FromDecimal(1.5m), ignoreCase: false));
    }

    [Fact]
    public void Guid_compares_by_equality_only()
    {
        Assert.True(QueryValueComparer.AreEqual(GuidA, QueryValue.FromGuid(GuidA), ignoreCase: false));
        Assert.False(QueryValueComparer.AreEqual(GuidA, QueryValue.FromGuid(GuidB), ignoreCase: false));

        // Incomparable (Guid vs string) is false for both == and !=.
        Assert.False(QueryValueComparer.Compare("x", QueryComparisonOperator.Equal, QueryValue.FromGuid(GuidA), ignoreCase: false));
        Assert.False(QueryValueComparer.Compare("x", QueryComparisonOperator.NotEqual, QueryValue.FromGuid(GuidA), ignoreCase: false));

        // No ordering: range operators evaluate false.
        Assert.False(QueryValueComparer.Compare(GuidA, QueryComparisonOperator.GreaterThan, QueryValue.FromGuid(GuidB), ignoreCase: false));
    }

    [Fact]
    public void Timestamp_orders_by_utc_instant_regardless_of_offset()
    {
        var noonUtc = new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero);
        var noonPlusTwo = new DateTimeOffset(2026, 6, 19, 14, 0, 0, TimeSpan.FromHours(2)); // same instant as noonUtc

        Assert.True(QueryValueComparer.AreEqual(noonPlusTwo, QueryValue.FromTimestamp(noonUtc), ignoreCase: false));
        Assert.True(QueryValueComparer.Compare(
            noonUtc.AddHours(1), QueryComparisonOperator.GreaterThan, QueryValue.FromTimestamp(noonUtc), ignoreCase: false));
    }

    // ---- JSON round-trip ----

    [Fact]
    public void New_kinds_round_trip_through_json_as_tagged_objects()
    {
        foreach (var value in new[]
        {
            QueryValue.FromGuid(GuidA),
            QueryValue.FromDecimal(1.100m),
            QueryValue.FromUnsignedInteger(ulong.MaxValue),
            QueryValue.FromTimestamp(new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero)),
        })
        {
            var json = JsonSerializer.Serialize(value, EventQueryJson.Options);
            Assert.Contains("\"kind\"", json);
            Assert.Equal(value, JsonRoundTrip(value));
        }
    }

    [Fact]
    public void Existing_kinds_keep_their_bare_scalar_wire_form()
    {
        Assert.Equal("5", JsonSerializer.Serialize(QueryValue.FromInteger(5), EventQueryJson.Options));
        Assert.Equal("\"x\"", JsonSerializer.Serialize(QueryValue.FromString("x"), EventQueryJson.Options));
        Assert.Equal("true", JsonSerializer.Serialize(QueryValue.FromBoolean(true), EventQueryJson.Options));
        Assert.Equal("null", JsonSerializer.Serialize(QueryValue.Null, EventQueryJson.Options));
    }

    [Fact]
    public void Unknown_tagged_kind_throws()
        => Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<QueryValue>("{\"kind\":\"bogus\",\"value\":\"x\"}", EventQueryJson.Options));

    // ---- text DSL round-trip ----

    [Fact]
    public void Decimal_and_unsigned_text_literals_parse_to_exact_kinds()
    {
        Assert.Equal(QueryValueKind.Decimal, QueryText.Parse("Price == 1.10m").Value!.Kind);
        Assert.Equal(QueryValueKind.UnsignedInteger, QueryText.Parse("Id == 42u").Value!.Kind);
        Assert.Equal(QueryValueKind.Integer, QueryText.Parse("Id == 42").Value!.Kind);
    }

    [Fact]
    public void Typed_text_literals_round_trip_canonically()
    {
        Assert.Equal("Price == 1.1m", QueryText.Format(QueryText.Parse("Price == 1.100m")));
        Assert.Equal("Price == 1m", QueryText.Format(QueryText.Parse("Price == 1.00m")));
        Assert.Equal("Id == 42u", QueryText.Format(QueryText.Parse("Id == 42u")));

        foreach (var text in new[] { "Price == 1.5m", "Id == 42u", $"Id == guid(\"{GuidA:D}\")", "At == ts(\"2026-06-19T12:00:00.0000000Z\")" })
        {
            Assert.Equal(Fingerprint(QueryText.Parse(text)), Fingerprint(QueryText.Parse(QueryText.Format(QueryText.Parse(text)))));
        }
    }

    [Fact]
    public void Malformed_typed_literals_are_rejected()
    {
        Assert.Throws<DotBoxD.Queryable.Translation.QueryTranslationException>(() => QueryText.Parse("Id == guid(\"not-a-guid\")"));
    }

    // ---- fingerprint canonicalization ----

    [Fact]
    public void Decimal_scale_does_not_affect_the_fingerprint()
    {
        Assert.Equal(
            Fingerprint(QueryFilter.Compare("Price", QueryComparisonOperator.Equal, QueryValue.FromDecimal(1.10m))),
            Fingerprint(QueryFilter.Compare("Price", QueryComparisonOperator.Equal, QueryValue.FromDecimal(1.100m))));
    }

    // ---- planner gate + dispatcher routing ----

    [Fact]
    public void Exact_kind_equality_is_routable_but_not_host_indexed()
    {
        var plan = EventQueryPlanner.Plan(
            ExpressionQueryTranslator.TranslateFilter<GuidEvent>(e => e.Id == GuidA));

        Assert.Contains(plan.RoutingKeys, p => p.Path == "Id");
        Assert.DoesNotContain(plan.IndexedPredicates, p => p.Path == "Id");
        Assert.NotEqual(IndexCoverage.Full, plan.Coverage); // host must re-verify
    }

    [Fact]
    public async Task Dispatcher_routes_a_guid_equality_subscription()
    {
        var host = new EventQueryHost();
        var hits = 0;

        await host.Query<GuidEvent>()
            .Where(e => e.Id == GuidA)
            .SubscribeAsync((_, _) =>
            {
                hits++;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        await host.PublishAsync(new GuidEvent(GuidA), context);
        await host.PublishAsync(new GuidEvent(GuidB), context);

        Assert.Equal(1, hits);
    }

    [Fact]
    public async Task Dispatcher_matches_an_exact_unsigned_id_above_two_pow_fifty_three()
    {
        var host = new EventQueryHost();
        var matched = new List<ulong>();
        const ulong big = 9007199254740993UL;

        await host.Query<BigIdEvent>()
            .Where(e => e.Id == big)
            .SubscribeAsync((e, _) =>
            {
                matched.Add(e.Id);
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);
        await host.PublishAsync(new BigIdEvent(big), context);
        await host.PublishAsync(new BigIdEvent(big - 1), context); // distinct id that collapses to the same double

        Assert.Equal(new[] { big }, matched);
    }

    [Fact]
    public void Offset_less_timestamp_is_rejected()
    {
        // The canonical form always carries 'Z'; an offset-less value would deserialize against the host's
        // local time zone, so both the JSON and text parsers reject it rather than capture a host-dependent instant.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<QueryValue>(
            "{\"kind\":\"timestamp\",\"value\":\"2026-06-19T12:00:00\"}", EventQueryJson.Options));
        Assert.Throws<DotBoxD.Queryable.Translation.QueryTranslationException>(
            () => QueryText.Parse("At == ts(\"2026-06-19T12:00:00\")"));
    }
}
