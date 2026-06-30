using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Interprets a portable <see cref="QueryFilter"/> directly against a runtime event object — the server-side
/// evaluation of the captured AST, with no compiled delegate required. Evaluation is bounded by
/// <see cref="QueryEvaluationLimits"/>; an AST that exceeds those bounds raises
/// <see cref="InvalidOperationException"/>.
/// </summary>
public static class QueryFilterEvaluator
{
    /// <summary>Evaluates <paramref name="filter"/> against <paramref name="target"/>.</summary>
    public static bool Evaluate(QueryFilter filter, object target, MemberValueReader reader)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(reader);
        var budget = QueryEvaluationLimits.MaxNodes;
        return EvaluateNode(filter, target, reader, depth: 0, ref budget);
    }

    /// <summary>Validates that <paramref name="filter"/> fits within <see cref="QueryEvaluationLimits"/>.</summary>
    public static void EnsureWithinLimits(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var budget = QueryEvaluationLimits.MaxNodes;
        Measure(filter, depth: 0, ref budget);
    }

    private static bool EvaluateNode(QueryFilter filter, object target, MemberValueReader reader, int depth, ref int budget)
    {
        CheckBudget(depth, ref budget);
        switch (filter.Kind)
        {
            case QueryFilterKind.MatchAll:
                return true;
            case QueryFilterKind.And:
                foreach (var child in filter.Children)
                {
                    if (!EvaluateNode(child, target, reader, depth + 1, ref budget))
                    {
                        return false;
                    }
                }

                return true;
            case QueryFilterKind.Or:
                foreach (var child in filter.Children)
                {
                    if (EvaluateNode(child, target, reader, depth + 1, ref budget))
                    {
                        return true;
                    }
                }

                return false;
            case QueryFilterKind.Not:
                return !EvaluateNode(filter.Children[0], target, reader, depth + 1, ref budget);
            case QueryFilterKind.Compare:
                return QueryValueComparer.Compare(
                    reader.Read(target, filter.Field),
                    filter.Operator,
                    QueryFilterInvariants.CompareValue(filter),
                    filter.IgnoreCase);
            case QueryFilterKind.In:
                return EvaluateIn(filter, target, reader);
            default:
                return false;
        }
    }

    private static bool EvaluateIn(QueryFilter filter, object target, MemberValueReader reader)
    {
        EnsureInWidth(filter);
        var actual = reader.Read(target, filter.Field);
        foreach (var candidate in filter.Values)
        {
            if (QueryValueComparer.AreEqual(actual, candidate, filter.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Measure(QueryFilter filter, int depth, ref int budget)
    {
        CheckBudget(depth, ref budget);
        if (filter.Kind == QueryFilterKind.Compare)
        {
            _ = QueryFilterInvariants.CompareValue(filter);
        }

        EnsureInWidth(filter);
        foreach (var child in filter.Children)
        {
            Measure(child, depth + 1, ref budget);
        }
    }

    // A wide In list never grows the node tree (its candidates live in Values, not Children), so it slips past
    // the node/depth budget while still forcing a per-event linear scan. Bound it explicitly.
    private static void EnsureInWidth(QueryFilter filter)
    {
        if (filter.Kind == QueryFilterKind.In && filter.Values.Count > QueryEvaluationLimits.MaxInValues)
        {
            throw new InvalidOperationException(
                $"Query filter 'In' list exceeds the maximum of {QueryEvaluationLimits.MaxInValues} values.");
        }
    }

    private static void CheckBudget(int depth, ref int budget)
    {
        if (depth > QueryEvaluationLimits.MaxDepth)
        {
            throw new InvalidOperationException($"Query filter exceeds the maximum depth of {QueryEvaluationLimits.MaxDepth}.");
        }

        if (--budget < 0)
        {
            throw new InvalidOperationException($"Query filter exceeds the maximum of {QueryEvaluationLimits.MaxNodes} nodes.");
        }
    }
}
