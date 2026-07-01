using System.Text.Json;
using DotBoxD.Queryable.Analysis;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryNullChildValidationTests
{
    [Theory]
    [InlineData(QueryFilterKind.And)]
    [InlineData(QueryFilterKind.Or)]
    public void Public_boolean_filter_initializer_with_null_child_is_rejected_by_satisfiability(
        QueryFilterKind kind)
    {
        var filter = BooleanWithNullChild(kind);

        AssertNullChildRejection(() => QuerySatisfiability.IsSatisfiable(filter), kind);
        AssertNullChildRejection(() => QuerySatisfiability.EnsureSatisfiable(filter), kind);
    }

    [Theory]
    [InlineData(QueryFilterKind.And)]
    [InlineData(QueryFilterKind.Or)]
    public void Public_boolean_filter_initializer_with_null_child_is_rejected_by_text_formatting(
        QueryFilterKind kind)
        => AssertNullChildRejection(() => QueryText.Format(BooleanWithNullChild(kind)), kind);

    [Theory]
    [InlineData(QueryFilterKind.And)]
    [InlineData(QueryFilterKind.Or)]
    public void Public_boolean_filter_initializer_with_null_child_is_rejected_by_json_serialization(
        QueryFilterKind kind)
        => AssertNullChildRejection(
            () => JsonSerializer.Serialize(BooleanWithNullChild(kind), EventQueryJson.Options),
            kind);

    [Theory]
    [InlineData(QueryFilterKind.And)]
    [InlineData(QueryFilterKind.Or)]
    public void Public_boolean_filter_initializer_with_null_child_is_rejected_by_planner(
        QueryFilterKind kind)
        => AssertNullChildRejection(() => EventQueryPlanner.Plan(BooleanWithNullChild(kind)), kind);

    [Theory]
    [InlineData(QueryFilterKind.And)]
    [InlineData(QueryFilterKind.Or)]
    public void Public_boolean_filter_initializer_with_null_child_is_rejected_by_evaluator(
        QueryFilterKind kind)
        => AssertNullChildRejection(
            () => QueryFilterEvaluator.Evaluate(
                BooleanWithNullChild(kind),
                new NullableTestEvent("key", 1),
                new MemberValueReader()),
            kind);

    [Theory]
    [InlineData(QueryFilterKind.And)]
    [InlineData(QueryFilterKind.Or)]
    public void Public_boolean_filter_initializer_with_null_child_is_rejected_by_compiler(
        QueryFilterKind kind)
        => AssertNullChildRejection(
            () => QueryFilterCompiler.Compile(BooleanWithNullChild(kind), new MemberValueReader()),
            kind);

    private static QueryFilter BooleanWithNullChild(QueryFilterKind kind)
        => new()
        {
            Kind = kind,
            Children = [QueryFilter.MatchAll, null!],
        };

    private static void AssertNullChildRejection(Action action, QueryFilterKind kind)
    {
        var exception = Assert.ThrowsAny<Exception>(action);
        Assert.True(
            exception is InvalidOperationException or JsonException,
            $"Expected InvalidOperationException or JsonException, got {exception.GetType().Name}: {exception.Message}");
        AssertNullChildMessage(exception, kind);
    }

    private static void AssertNullChildMessage(Exception exception, QueryFilterKind kind)
    {
        Assert.Contains("QueryFilter", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"QueryFilter {kind} nodes", exception.Message, StringComparison.Ordinal);
        Assert.True(
            exception.Message.Contains("child", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("null", StringComparison.OrdinalIgnoreCase),
            $"Expected a child/null validation message, but got: {exception.Message}");
    }
}
