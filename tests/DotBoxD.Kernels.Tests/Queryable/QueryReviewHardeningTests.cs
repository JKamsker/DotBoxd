using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

/// <summary>
/// Pins the PR-review hardening fixes across the queryable surface: empty-disjunction semantics, value
/// equality provenance, ulong enums, string-comparison/comparer rejection, In-list and parse-depth bounds,
/// projection field validation, read-only serializer options, and dispatch isolation.
/// </summary>
public sealed class QueryReviewHardeningTests
{
    private enum UlongBackedEnum : ulong
    {
        Max = ulong.MaxValue,
    }

    [Fact]
    public void Empty_disjunction_is_never_match_while_empty_conjunction_is_match_all()
    {
        var or = QueryFilter.Or([]);
        Assert.Equal(QueryFilterKind.Not, or.Kind);
        Assert.Equal(QueryFilterKind.MatchAll, or.Children[0].Kind);

        Assert.Equal(QueryFilterKind.MatchAll, QueryFilter.And([]).Kind);
    }

    [Fact]
    public void Value_equality_ignores_capture_provenance()
    {
        var a = QueryValue.FromInteger(5) with { ParameterKey = "p0" };
        var b = QueryValue.FromInteger(5) with { ParameterKey = "p1" };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Ulong_backed_enum_constant_is_captured_as_an_exact_unsigned_integer()
    {
        Assert.True(QueryValue.TryFromObject(UlongBackedEnum.Max, out var value));
        Assert.Equal(QueryValueKind.UnsignedInteger, value.Kind);
        Assert.Equal(ulong.MaxValue, value.UnsignedInteger);
    }

    [Fact]
    public void Culture_sensitive_string_comparison_is_rejected()
        => Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
                e => e.TargetId.StartsWith("x", StringComparison.InvariantCultureIgnoreCase)));

    [Fact]
    public void Legacy_culture_sensitive_string_overload_is_rejected()
        => Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
                e => e.TargetId.StartsWith("PL", ignoreCase: true, CultureInfo.InvariantCulture)));

    [Fact]
    public void One_argument_starts_with_is_rejected_because_it_is_culture_sensitive()
        => Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
                e => e.TargetId.StartsWith("caf\u00e9")));

    [Fact]
    public void One_argument_ends_with_is_rejected_because_it_is_culture_sensitive()
        => Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
                e => e.TargetId.EndsWith("caf\u00e9")));

    [Fact]
    public void Contains_over_a_case_insensitive_collection_is_rejected()
    {
        var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };
        Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId)));
    }

    [Fact]
    public void Static_contains_over_a_case_insensitive_collection_is_rejected()
    {
        var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b" };
        Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
                e => Enumerable.Contains(watched, e.AttackerId)));
    }

    [Fact]
    public void Contains_over_a_default_collection_still_lowers_to_in()
    {
        var ids = new[] { "a", "b" };
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => ids.Contains(e.AttackerId));
        Assert.Equal(QueryFilterKind.In, filter.Kind);
    }

    [Fact]
    public void Oversized_in_list_is_rejected_by_the_limit_check()
    {
        var tooMany = Enumerable.Range(0, QueryEvaluationLimits.MaxInValues + 1)
            .Select(i => QueryValue.FromInteger(i))
            .ToList();
        Assert.Throws<InvalidOperationException>(() => QueryFilterEvaluator.EnsureWithinLimits(QueryFilter.In("Damage", tooMany)));

        var withinLimit = QueryFilter.In("Damage", Enumerable.Range(0, 4).Select(i => QueryValue.FromInteger(i)).ToList());
        QueryFilterEvaluator.EnsureWithinLimits(withinLimit); // does not throw
    }

    [Fact]
    public void Public_compare_filter_initializer_without_value_is_rejected()
    {
        var filter = new QueryFilter
        {
            Kind = QueryFilterKind.Compare,
            Field = "Key",
            Operator = QueryComparisonOperator.Equal,
        };

        var evaluation = Assert.Throws<InvalidOperationException>(() =>
            QueryFilterEvaluator.Evaluate(filter, new NullableTestEvent(null, 1), new MemberValueReader()));
        Assert.Contains("Compare", evaluation.Message, StringComparison.Ordinal);
        Assert.Contains("Value", evaluation.Message, StringComparison.Ordinal);

        var formatting = Assert.Throws<InvalidOperationException>(() => QueryText.Format(filter));
        Assert.Contains("Compare", formatting.Message, StringComparison.Ordinal);
        Assert.Contains("Value", formatting.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => QueryFilterCompiler.Compile(filter, new MemberValueReader()));
        Assert.Throws<InvalidOperationException>(() => EventQueryPlanner.Plan(filter));
        Assert.Throws<InvalidOperationException>(() => JsonSerializer.Serialize(filter, EventQueryJson.Options));
    }

    [Fact]
    public void Deeply_nested_text_is_rejected_instead_of_overflowing_the_stack()
    {
        var deep = string.Concat(Enumerable.Repeat("not ", 2000)) + "AttackerId == \"a\"";
        Assert.Throws<QueryTranslationException>(() => QueryText.Parse(deep));
    }

    [Fact]
    public void Projection_field_must_have_exactly_one_of_path_or_value()
    {
        var validJson = JsonSerializer.Serialize(
            QueryProjection.Construct("T", [QueryProjectionField.FromMember("a", "X")]),
            EventQueryJson.Options);

        var both = validJson.Replace("\"path\":\"X\"", "\"path\":\"X\",\"value\":1");
        var neither = validJson.Replace("\"path\":\"X\"", "\"other\":1");

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<QueryProjection>(both, EventQueryJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<QueryProjection>(neither, EventQueryJson.Options));
    }

    [Fact]
    public void Public_member_projection_initializer_without_path_is_rejected_on_write()
    {
        var projection = new QueryProjection { Kind = QueryProjectionKind.Member };

        var exception = Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Serialize(projection, EventQueryJson.Options));
        Assert.True(exception is JsonException or InvalidOperationException);
        Assert.Contains("Member", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shared_serializer_options_are_read_only()
    {
        Assert.True(EventQueryJson.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => EventQueryJson.Options.Converters.Add(new JsonStringEnumConverter()));
    }

    [Fact]
    public async Task One_failing_query_handler_does_not_starve_the_others()
    {
        var host = new EventQueryHost();
        var second = 0;

        await host.Query<AttackTestEvent>()
            .SubscribeAsync((_, _) => throw new InvalidOperationException("boom"));
        await host.Query<AttackTestEvent>()
            .SubscribeAsync((_, _) =>
            {
                second++;
                return ValueTask.CompletedTask;
            });

        var context = new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None);

        // The first subscriber throws; the dispatcher must isolate it so the second still runs and the
        // publish does not surface the handler exception.
        await host.PublishAsync(new AttackTestEvent("a", "b", 1, 1), context);

        Assert.Equal(1, second);
    }

    [Fact]
    public void Contains_over_a_factory_created_culture_comparer_is_rejected()
    {
        // Not a public StringComparer singleton, so an identity check would miss it; the behavioral probe
        // catches it because it reports "a" == "A".
        var watched = new HashSet<string>(StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)) { "a" };
        Assert.Throws<QueryTranslationException>(() =>
            ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId)));
    }

    [Fact]
    public void Contains_over_a_default_hashset_still_lowers_to_in()
    {
        var watched = new HashSet<string> { "a", "b" };
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => watched.Contains(e.AttackerId));
        Assert.Equal(QueryFilterKind.In, filter.Kind);
    }

    [Fact]
    public void Query_filter_in_snapshots_its_values()
    {
        var values = new List<QueryValue> { QueryValue.FromInteger(1) };
        var filter = QueryFilter.In("Damage", values);
        values.Add(QueryValue.FromInteger(2));
        Assert.Single(filter.Values);
    }

    [Fact]
    public void Query_projection_construct_snapshots_its_fields()
    {
        var fields = new List<QueryProjectionField> { QueryProjectionField.FromMember("a", "X") };
        var projection = QueryProjection.Construct("T", fields);
        fields.Add(QueryProjectionField.FromMember("b", "Y"));
        Assert.Single(projection.Fields);
    }
}
