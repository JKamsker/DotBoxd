using DotBoxD.Queryable.Analysis;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Authoring;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QuerySatisfiabilityTests
{
    [Theory]
    [MemberData(nameof(Contradictions))]
    public void Contradictory_filters_are_unsatisfiable(QueryFilter filter)
        => Assert.False(QuerySatisfiability.IsSatisfiable(filter));

    [Theory]
    [MemberData(nameof(Satisfiable))]
    public void Consistent_filters_are_satisfiable(QueryFilter filter)
        => Assert.True(QuerySatisfiability.IsSatisfiable(filter));

    [Fact]
    public void Conflicting_equality_from_expression_is_rejected()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.Damage == 1 && e.Damage == 2);
        Assert.False(QuerySatisfiability.IsSatisfiable(filter));
    }

    [Fact]
    public void Equality_outside_range_from_expression_is_rejected()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(e => e.Damage == 1 && e.Damage >= 5);
        Assert.False(QuerySatisfiability.IsSatisfiable(filter));
    }

    [Fact]
    public void Distinct_large_integer_equality_and_inequality_are_satisfiable()
    {
        var filter = QueryFilter.And([
            QueryFilter.Compare("Id", QueryComparisonOperator.Equal, QueryValue.FromInteger(9007199254740993L)),
            QueryFilter.Compare("Id", QueryComparisonOperator.NotEqual, QueryValue.FromInteger(9007199254740992L)),
        ]);

        Assert.True(QuerySatisfiability.IsSatisfiable(filter));
    }

    [Fact]
    public void Distinct_large_integer_equalities_are_unsatisfiable()
    {
        var filter = QueryFilter.And([
            QueryFilter.Compare("Id", QueryComparisonOperator.Equal, QueryValue.FromInteger(9007199254740993L)),
            QueryFilter.Compare("Id", QueryComparisonOperator.Equal, QueryValue.FromInteger(9007199254740992L)),
        ]);

        Assert.False(QuerySatisfiability.IsSatisfiable(filter));
    }

    [Fact]
    public void Term_and_its_negation_is_rejected()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<SourcedTestEvent>(e => e.Flagged && !e.Flagged);
        Assert.False(QuerySatisfiability.IsSatisfiable(filter));
    }

    [Fact]
    public async Task Subscribing_a_contradictory_query_throws_fast()
    {
        var host = new EventQueryHost();
        var ex = await Assert.ThrowsAsync<QueryTranslationException>(async () =>
            await host.Query<AttackTestEvent>()
                .Where(e => e.AttackerId == "a")
                .Where(e => e.AttackerId == "b")
                .SubscribeAsync((_, _) => ValueTask.CompletedTask));

        Assert.Contains("contradictory", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(host.HasSubscriptions<AttackTestEvent>());
    }

    public static TheoryData<QueryFilter> Contradictions() => new()
    {
        // x == 1 && x == 2
        QueryFilter.And([Eq("x", 1), Eq("x", 2)]),
        // x == "a" && x != "a"
        QueryFilter.And([
            QueryFilter.Compare("x", QueryComparisonOperator.Equal, QueryValue.FromString("a")),
            QueryFilter.Compare("x", QueryComparisonOperator.NotEqual, QueryValue.FromString("a")),
        ]),
        // x >= 5 && x <= 1
        QueryFilter.And([
            QueryFilter.Compare("x", QueryComparisonOperator.GreaterThanOrEqual, QueryValue.FromInteger(5)),
            QueryFilter.Compare("x", QueryComparisonOperator.LessThanOrEqual, QueryValue.FromInteger(1)),
        ]),
        // x > 5 && x < 5  (empty open interval)
        QueryFilter.And([
            QueryFilter.Compare("x", QueryComparisonOperator.GreaterThan, QueryValue.FromInteger(5)),
            QueryFilter.Compare("x", QueryComparisonOperator.LessThan, QueryValue.FromInteger(5)),
        ]),
        // empty IN
        QueryFilter.In("x", []),
    };

    public static TheoryData<QueryFilter> Satisfiable() => new()
    {
        QueryFilter.MatchAll,
        Eq("x", 1),
        QueryFilter.And([Eq("x", 1), Eq("y", 2)]),
        // x >= 1 && x <= 5 && x == 3
        QueryFilter.And([
            QueryFilter.Compare("x", QueryComparisonOperator.GreaterThanOrEqual, QueryValue.FromInteger(1)),
            QueryFilter.Compare("x", QueryComparisonOperator.LessThanOrEqual, QueryValue.FromInteger(5)),
            Eq("x", 3),
        ]),
        // disjunction with one satisfiable branch
        QueryFilter.Or([QueryFilter.And([Eq("x", 1), Eq("x", 2)]), Eq("y", 9)]),
    };

    private static QueryFilter Eq(string field, long value)
        => QueryFilter.Compare(field, QueryComparisonOperator.Equal, QueryValue.FromInteger(value));
}
