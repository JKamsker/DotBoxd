using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Planning;

/// <summary>
/// Extracts an <see cref="EventQueryPlan"/> from a filter AST. The planner recognizes a conjunction (or a
/// single leaf) of scalar comparisons. Equality and range comparisons over a <em>host-indexable</em> kind
/// (bool/integer/number/string) become index-covered predicates; equality over <em>any</em> non-null kind —
/// including the exact kinds (Guid/Decimal/UnsignedInteger/Timestamp) the host index vocabulary cannot carry —
/// becomes a routing key for the in-process dispatcher's own equality index. Anything not host-indexable
/// (negations, string matches, set membership, disjunction/nesting, and the exact kinds) stays in the residual
/// filter so the host re-verifies it. Filters that are not a conjunction of leaves are treated as fully residual.
/// </summary>
public static class EventQueryPlanner
{
    /// <summary>Plans the filter of a document.</summary>
    public static EventQueryPlan Plan(EventQueryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Plan(document.Filter);
    }

    /// <summary>Plans a filter AST into index-covered predicates plus a residual.</summary>
    public static EventQueryPlan Plan(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        QueryFilterInvariants.RequireValidShape(filter);

        if (filter.Kind == QueryFilterKind.MatchAll)
        {
            return new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
                ResidualFilter = null,
                Coverage = IndexCoverage.Full,
            };
        }

        if (filter.Kind is QueryFilterKind.Or or QueryFilterKind.Not)
        {
            return new EventQueryPlan
            {
                IndexedPredicates = [],
                RoutingKeys = [],
                ResidualFilter = filter,
                Coverage = IndexCoverage.None,
            };
        }

        var terms = filter.Kind == QueryFilterKind.And ? filter.Children : [filter];
        var indexed = new List<IndexedPredicate>();
        var routing = new List<IndexedPredicate>();
        var residual = new List<QueryFilter>();

        foreach (var term in terms)
        {
            // Host-indexable predicates feed the host index AND are fully covered there; everything else stays
            // residual so the host re-verifies it. Equality of any non-null kind still becomes a routing key
            // for the dispatcher's own index, even when the host cannot carry that kind.
            var hostIndexed = TryHostIndex(term, out var hostPredicate);
            if (hostIndexed)
            {
                indexed.Add(hostPredicate);
            }

            if (TryRoutingKey(term, out var routeKey))
            {
                routing.Add(routeKey);
            }

            if (!hostIndexed)
            {
                residual.Add(term);
            }
        }

        var coverage = residual.Count == 0
            ? IndexCoverage.Full
            : indexed.Count == 0 ? IndexCoverage.None : IndexCoverage.Partial;

        return new EventQueryPlan
        {
            IndexedPredicates = indexed,
            RoutingKeys = routing,
            ResidualFilter = residual.Count == 0 ? null : QueryFilter.And(residual),
            Coverage = coverage,
        };
    }

    // A host-indexable predicate: a non-ignore-case comparison whose value kind the host index vocabulary
    // accepts (bool/int/long/double/string) under an equality or range operator.
    private static bool TryHostIndex(QueryFilter term, out IndexedPredicate predicate)
    {
        predicate = null!;
        if (term.Kind != QueryFilterKind.Compare || term.Value is null || term.IgnoreCase)
        {
            return false;
        }

        if (!IsHostIndexableKind(term.Value.Kind) || !IsIndexableOperator(term.Operator))
        {
            return false;
        }

        predicate = new IndexedPredicate
        {
            Path = term.Field,
            Operator = term.Operator,
            Value = term.Value,
        };
        return true;
    }

    // Equality over any non-null kind is routable through the dispatcher's own composite index, independent of
    // the host index vocabulary — so Guid/Decimal/UnsignedInteger/Timestamp equality still gets prefiltering.
    private static bool TryRoutingKey(QueryFilter term, out IndexedPredicate predicate)
    {
        predicate = null!;
        if (term.Kind != QueryFilterKind.Compare ||
            term.Value is null ||
            term.IgnoreCase ||
            term.Value.Kind == QueryValueKind.Null ||
            term.Operator != QueryComparisonOperator.Equal)
        {
            return false;
        }

        predicate = new IndexedPredicate
        {
            Path = term.Field,
            Operator = term.Operator,
            Value = term.Value,
        };
        return true;
    }

    // The value kinds the host index (DotBoxD.Plugins EventIndexMatcher) can carry. The exact kinds
    // (Guid/Decimal/UnsignedInteger/Timestamp) and Null are excluded — they route in-process only.
    private static bool IsHostIndexableKind(QueryValueKind kind) => kind switch
    {
        QueryValueKind.Boolean or
            QueryValueKind.Integer or
            QueryValueKind.Number or
            QueryValueKind.String => true,
        _ => false,
    };

    private static bool IsIndexableOperator(QueryComparisonOperator op) => op switch
    {
        QueryComparisonOperator.Equal => true,
        QueryComparisonOperator.GreaterThan => true,
        QueryComparisonOperator.GreaterThanOrEqual => true,
        QueryComparisonOperator.LessThan => true,
        QueryComparisonOperator.LessThanOrEqual => true,
        _ => false,
    };
}
