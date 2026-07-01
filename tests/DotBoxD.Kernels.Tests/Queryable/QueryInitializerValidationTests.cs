using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryInitializerValidationTests
{
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
    public void Public_filter_initializer_with_undefined_kind_is_rejected_by_evaluator()
    {
        var filter = new QueryFilter { Kind = (QueryFilterKind)999 };

        var evaluation = Assert.ThrowsAny<Exception>(() =>
            QueryFilterEvaluator.Evaluate(filter, new NullableTestEvent(null, 1), new MemberValueReader()));
        Assert.True(evaluation is InvalidOperationException or ArgumentOutOfRangeException);
        Assert.Contains("kind", evaluation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("999", evaluation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Public_filter_initializer_with_undefined_kind_is_rejected_by_compiler()
    {
        var filter = new QueryFilter { Kind = (QueryFilterKind)999 };

        var compilation = Assert.ThrowsAny<Exception>(() =>
            QueryFilterCompiler.Compile(filter, new MemberValueReader()));
        Assert.True(compilation is InvalidOperationException or ArgumentOutOfRangeException);
        Assert.Contains("kind", compilation.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("999", compilation.Message, StringComparison.Ordinal);
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
}
