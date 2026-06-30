namespace DotBoxD.Queryable.Ast;

internal static class QueryFilterInvariants
{
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
}
