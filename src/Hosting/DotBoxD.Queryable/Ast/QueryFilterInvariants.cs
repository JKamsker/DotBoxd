namespace DotBoxD.Queryable.Ast;

internal static class QueryFilterInvariants
{
    public static QueryFilterKind RequireKnownKind(QueryFilter filter)
        => filter.Kind switch
        {
            QueryFilterKind.MatchAll or
            QueryFilterKind.And or
            QueryFilterKind.Or or
            QueryFilterKind.Not or
            QueryFilterKind.Compare or
            QueryFilterKind.In => filter.Kind,
            _ => throw UnknownKind(filter.Kind),
        };

    public static QueryValue CompareValue(QueryFilter filter)
        => filter.Value ?? throw MissingCompareValue();

    public static void RequireCompareValues(QueryFilter filter)
    {
        if (filter.Kind == QueryFilterKind.Compare)
        {
            _ = CompareValue(filter);
            return;
        }

        foreach (var child in filter.Children)
        {
            RequireCompareValues(child);
        }
    }

    private static InvalidOperationException MissingCompareValue()
        => new("QueryFilter Compare nodes require Value.");

    private static InvalidOperationException UnknownKind(QueryFilterKind kind)
        => new($"QueryFilter has unsupported Kind value '{(int)kind}'.");
}
