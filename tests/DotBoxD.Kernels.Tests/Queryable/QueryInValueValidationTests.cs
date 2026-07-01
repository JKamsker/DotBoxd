using System.Text.Json;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Planning;
using DotBoxD.Queryable.Serialization;
using DotBoxD.Queryable.Text;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryInValueValidationTests
{
    private static readonly MemberValueReader Reader = new();

    public static TheoryData<string, Action<QueryFilter>> NullValuePublicPaths => new()
    {
        { "text formatting", filter => QueryText.Format(filter) },
        { "JSON serialization", filter => JsonSerializer.Serialize(filter, EventQueryJson.Options) },
        { "planning", filter => EventQueryPlanner.Plan(filter) },
        { "limit validation", QueryFilterEvaluator.EnsureWithinLimits },
        {
            "interpreter with nonmatching value",
            filter => QueryFilterEvaluator.Evaluate(filter, new InValueTestEvent("missing"), Reader)
        },
        {
            "interpreter with null value",
            filter => QueryFilterEvaluator.Evaluate(filter, new InValueTestEvent(null), Reader)
        },
        {
            "compiled predicate with nonmatching value",
            filter => QueryFilterCompiler.Compile(filter, Reader)(new InValueTestEvent("missing"))
        },
        {
            "compiled predicate with null value",
            filter => QueryFilterCompiler.Compile(filter, Reader)(new InValueTestEvent(null))
        },
    };

    [Theory]
    [MemberData(nameof(NullValuePublicPaths))]
    public void Public_in_filter_initializer_with_null_value_is_rejected(
        string path,
        Action<QueryFilter> action)
    {
        var exception = Record.Exception(() => action(InWithNullValue()));

        Assert.NotNull(exception);
        Assert.True(
            exception is InvalidOperationException or JsonException,
            $"Expected InvalidOperationException or JsonException from {path}, got {exception.GetType()}: {exception.Message}");
        Assert.Contains("QueryFilter", exception.Message, StringComparison.Ordinal);
        Assert.Contains("In", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Values", exception.Message, StringComparison.Ordinal);
        Assert.Contains("null", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static QueryFilter InWithNullValue()
        => new()
        {
            Kind = QueryFilterKind.In,
            Field = nameof(InValueTestEvent.Key),
            Values = [QueryValue.FromString("a"), null!],
        };

    private sealed record InValueTestEvent(string? Key);
}
