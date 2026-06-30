namespace DotBoxD.Queryable.Ast;

internal static class QueryProjectionInvariants
{
    public static string MemberPath(QueryProjection projection)
        => NonEmpty(projection.Path, "QueryProjection Member nodes require Path.");

    public static string ConstructTypeName(QueryProjection projection)
        => NonEmpty(projection.TypeName, "QueryProjection Construct nodes require TypeName.");

    public static string FieldName(QueryProjectionField field)
        => NonEmpty(field.Name, "QueryProjection Construct fields require Name.");

    public static bool FieldHasPath(QueryProjectionField field)
    {
        var hasPath = !string.IsNullOrEmpty(field.Path);
        var hasConstant = field.Constant is not null;
        if (hasPath == hasConstant)
        {
            throw new InvalidOperationException(
                $"QueryProjection Construct field '{field.Name}' must contain exactly one of Path or Constant.");
        }

        return hasPath;
    }

    public static string FieldPath(QueryProjectionField field)
        => NonEmpty(field.Path, $"QueryProjection Construct field '{field.Name}' requires Path.");

    public static QueryValue FieldConstant(QueryProjectionField field)
        => field.Constant ?? throw new InvalidOperationException(
            $"QueryProjection Construct field '{field.Name}' requires Constant.");

    private static string NonEmpty(string? value, string message)
        => string.IsNullOrEmpty(value) ? throw new InvalidOperationException(message) : value;
}
