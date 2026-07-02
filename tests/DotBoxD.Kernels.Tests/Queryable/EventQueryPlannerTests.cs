using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class EventQueryPlannerTests
{
    [Fact]
    public void Plan_extracts_equality_and_range_as_full_coverage()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.AttackerId == "player-1" && e.Damage >= 5);

        var plan = EventQueryPlanner.Plan(filter);

        Assert.Equal(IndexCoverage.Full, plan.Coverage);
        Assert.Null(plan.ResidualFilter);
        Assert.Equal(2, plan.IndexedPredicates.Count);
        Assert.Single(plan.RoutingKeys);
        Assert.Equal("AttackerId", plan.RoutingKeys[0].Path);
        Assert.True(plan.IsRoutable);

        var range = plan.IndexedPredicates.Single(p => p.Path == "Damage");
        Assert.Equal(QueryComparisonOperator.GreaterThanOrEqual, range.Operator);
    }

    [Fact]
    public void Plan_flattens_nested_conjunction_and_indexes_every_leaf()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.AttackerId == "player-1" && e.TargetId == "monster-1" && e.Damage >= 5);

        // The left-associative chain must flatten to a single And so the planner sees every leaf.
        Assert.Equal(QueryFilterKind.And, filter.Kind);
        Assert.Equal(3, filter.Children.Count);

        var plan = EventQueryPlanner.Plan(filter);

        Assert.Equal(IndexCoverage.Full, plan.Coverage);
        Assert.Equal(3, plan.IndexedPredicates.Count);
        Assert.Equal(2, plan.RoutingKeys.Count);
        Assert.Null(plan.ResidualFilter);
    }

    [Fact]
    public void Plan_keeps_unindexable_terms_as_residual_partial_coverage()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.AttackerId == "player-1" && e.TargetId.StartsWith("player-", StringComparison.Ordinal));

        var plan = EventQueryPlanner.Plan(filter);

        Assert.Equal(IndexCoverage.Partial, plan.Coverage);
        Assert.Single(plan.IndexedPredicates);
        Assert.NotNull(plan.ResidualFilter);
        Assert.Equal(QueryFilterKind.Compare, plan.ResidualFilter!.Kind);
        Assert.Equal(QueryComparisonOperator.StringStartsWith, plan.ResidualFilter!.Operator);
    }

    [Fact]
    public void Plan_treats_disjunction_as_fully_residual()
    {
        var filter = ExpressionQueryTranslator.TranslateFilter<AttackTestEvent>(
            e => e.AttackerId == "player-1" || e.Damage >= 5);

        var plan = EventQueryPlanner.Plan(filter);

        Assert.Equal(IndexCoverage.None, plan.Coverage);
        Assert.Empty(plan.IndexedPredicates);
        Assert.Empty(plan.RoutingKeys);
        Assert.NotNull(plan.ResidualFilter);
    }

    [Fact]
    public void Plan_for_match_all_is_fully_covered_without_routing()
    {
        var plan = EventQueryPlanner.Plan(QueryFilter.MatchAll);

        Assert.Equal(IndexCoverage.Full, plan.Coverage);
        Assert.False(plan.IsRoutable);
        Assert.Null(plan.ResidualFilter);
    }
}
