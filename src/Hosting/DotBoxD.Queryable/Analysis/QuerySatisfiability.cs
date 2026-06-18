using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Analysis;

/// <summary>
/// Decides whether a filter can ever match — a sound (not complete) check that rejects clear
/// contradictions so a host fails a never-matching subscription fast instead of registering dead weight.
/// It recognizes: an empty <see cref="QueryFilterKind.In"/> set, conflicting equalities on a field
/// (<c>x == a &amp;&amp; x == b</c>), an equality contradicted by an inequality (<c>x == v &amp;&amp; x != v</c>), a
/// term and its negation (<c>x &amp;&amp; !x</c>), and contradictory or equality-excluding numeric ranges
/// (<c>x &gt;= 5 &amp;&amp; x &lt;= 1</c>, <c>x == 1 &amp;&amp; x &gt;= 5</c>). Anything it cannot prove contradictory is treated as
/// satisfiable.
/// </summary>
public static class QuerySatisfiability
{
    /// <summary>Returns <see langword="false"/> only when <paramref name="filter"/> is provably never-matching.</summary>
    public static bool IsSatisfiable(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return !IsContradiction(filter);
    }

    /// <summary>Throws <see cref="Translation.QueryTranslationException"/> when <paramref name="filter"/> can never match.</summary>
    public static void EnsureSatisfiable(QueryFilter filter)
    {
        if (!IsSatisfiable(filter))
        {
            throw new Translation.QueryTranslationException(
                "The query filter is contradictory and can never match any event; remove the conflicting predicates.");
        }
    }

    private static bool IsContradiction(QueryFilter filter) => filter.Kind switch
    {
        QueryFilterKind.MatchAll => false,
        QueryFilterKind.In => filter.Values.Count == 0,
        QueryFilterKind.Not => IsTautology(filter.Children[0]),
        QueryFilterKind.Or => filter.Children.Count > 0 && filter.Children.All(IsContradiction),
        QueryFilterKind.And => AndIsContradiction(filter),
        _ => false,
    };

    private static bool IsTautology(QueryFilter filter)
        => filter.Kind == QueryFilterKind.MatchAll;

    private static bool AndIsContradiction(QueryFilter filter)
    {
        foreach (var child in filter.Children)
        {
            if (IsContradiction(child))
            {
                return true;
            }
        }

        return ConjunctionContradictions.Detect(filter.Children);
    }
}
