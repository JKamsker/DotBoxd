using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Planning;
using HostIndexedPredicate = DotBoxD.Plugins.IndexedPredicate;
using HostIndexPredicateOperator = DotBoxD.Plugins.IndexPredicateOperator;

namespace DotBoxD.Queryable.Integration;

/// <summary>
/// Bridges a queryable <see cref="EventQueryPlan"/> onto the framework's first-class host dispatch index
/// (<c>DotBoxD.Plugins.Indexing.EventIndexRegistry</c> / <c>EventIndexMatcher{TEvent}</c>, issue #50). It
/// translates the plan's index-covered <see cref="DotBoxD.Queryable.Planning.IndexedPredicate"/>s — which
/// <see cref="EventQueryPlanner"/> already proved are <i>necessary</i> AND conditions of the filter — into
/// the host <see cref="HostIndexedPredicate"/> shape, so a dynamic queryable subscription can prefilter
/// through the same equality/range buckets as an analyzer-lowered <c>.Where(...).Run(...)</c> kernel chain
/// instead of maintaining a parallel index.
/// <para>
/// Pair the result with <c>plan.Coverage == IndexCoverage.Full</c> as the <c>indexCoversPredicate</c>
/// argument of <c>EventIndexRegistry.Register</c>: every produced predicate is a necessary condition, so
/// the host may reject on any of them safely regardless of coverage, and may skip the verified authority
/// only when the index fully covers the filter (which the registry additionally re-checks).
/// </para>
/// <para>
/// <b>Type fidelity.</b> A portable <see cref="QueryValue"/> erases CLR identity: every integral type
/// collapses to <see cref="QueryValueKind.Integer"/> (a <see cref="long"/>) and
/// <see cref="float"/>/<see cref="double"/>/<see cref="decimal"/>/<see cref="ulong"/> to
/// <see cref="QueryValueKind.Number"/> (a <see cref="double"/>). The emitted
/// <see cref="HostIndexedPredicate.ValueType"/> therefore reflects the portable value kind
/// (<c>bool</c>/<c>long</c>/<c>double</c>/<c>string</c>), not the event's declared property type. The host's
/// <c>EventIndexMatcher</c> reconciles the boxed value to the real property CLR type when it compiles the
/// index, so an integral bound still matches an <c>int</c>/<c>short</c>/… field. The same widening means the
/// registry's double-typed carve-out (which keeps the verified IR authoritative for <c>double</c> keys)
/// triggers exactly when the captured literal is floating-point.
/// </para>
/// </summary>
public static class EventQueryIndexAdapter
{
    // The manifest value-type tokens. The canonical list (DotBoxD.Plugins.Analyzer ManifestTypes) is
    // internal to the analyzer assembly; these are the same wire tokens EventIndexMatcher.AnyDoubleTyped
    // and the plugin-package serializer expect.
    private const string BoolType = "bool";
    private const string LongType = "long";
    private const string DoubleType = "double";
    private const string StringType = "string";

    /// <summary>
    /// Translates every index-covered predicate of <paramref name="plan"/> into the host index shape. The
    /// equality subset is what the registry buckets as routing keys; range predicates describe indexable
    /// bounds.
    /// </summary>
    public static IReadOnlyList<HostIndexedPredicate> ToIndexedPredicates(EventQueryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ToIndexedPredicates(plan.IndexedPredicates);
    }

    /// <summary>Translates a list of planner predicates into the host index shape, preserving order.</summary>
    public static IReadOnlyList<HostIndexedPredicate> ToIndexedPredicates(IReadOnlyList<IndexedPredicate> predicates)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        var result = new HostIndexedPredicate[predicates.Count];
        for (var i = 0; i < predicates.Count; i++)
        {
            result[i] = ToIndexedPredicate(predicates[i]);
        }

        return result;
    }

    /// <summary>
    /// Translates a single planner predicate. Throws <see cref="NotSupportedException"/> for operators or
    /// value kinds that are not index-eligible (the string-match operators and a <c>null</c> bound). The
    /// <see cref="EventQueryPlanner"/> never places those in <see cref="EventQueryPlan.IndexedPredicates"/>,
    /// so a plan produced by the planner always converts without throwing; the guard exists for hand-built
    /// predicates, where failing loud is safer than silently dropping a necessary condition.
    /// </summary>
    public static HostIndexedPredicate ToIndexedPredicate(IndexedPredicate source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (value, valueType) = ConvertValue(source.Value);
        return new HostIndexedPredicate(source.Path, ConvertOperator(source.Operator), value, valueType);
    }

    private static HostIndexPredicateOperator ConvertOperator(QueryComparisonOperator op) => op switch
    {
        QueryComparisonOperator.Equal => HostIndexPredicateOperator.Equals,
        QueryComparisonOperator.NotEqual => HostIndexPredicateOperator.NotEquals,
        QueryComparisonOperator.GreaterThan => HostIndexPredicateOperator.GreaterThan,
        QueryComparisonOperator.GreaterThanOrEqual => HostIndexPredicateOperator.GreaterThanOrEqual,
        QueryComparisonOperator.LessThan => HostIndexPredicateOperator.LessThan,
        QueryComparisonOperator.LessThanOrEqual => HostIndexPredicateOperator.LessThanOrEqual,
        _ => throw new NotSupportedException(
            $"Query operator '{op}' is not index-eligible and cannot be mapped to a host index predicate."),
    };

    private static (object? Value, string ValueType) ConvertValue(QueryValue value) => value.Kind switch
    {
        QueryValueKind.Boolean => ((object?)value.Boolean, BoolType),
        QueryValueKind.Integer => ((object?)value.Integer, LongType),
        QueryValueKind.Number => ((object?)value.Number, DoubleType),
        QueryValueKind.String => value.String is { } text
            ? ((object?)text, StringType)
            : throw new NotSupportedException("A null string bound is not index-eligible."),
        _ => throw new NotSupportedException(
            $"Query value kind '{value.Kind}' is not index-eligible and cannot be mapped to a host index predicate."),
    };
}
