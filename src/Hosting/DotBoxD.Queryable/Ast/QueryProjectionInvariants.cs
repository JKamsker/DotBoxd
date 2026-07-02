namespace DotBoxD.Queryable.Ast;

internal static class QueryProjectionInvariants
{
    public static void RequireValidShape(QueryProjection projection)
    {
        switch (projection.Kind)
        {
            case QueryProjectionKind.Identity:
                RejectInactiveArmProperties(
                    projection,
                    QueryProjectionKind.Identity,
                    hasPath: true,
                    hasTypeName: true,
                    hasFields: true);
                break;
            case QueryProjectionKind.Member:
                _ = MemberPath(projection);
                RejectInactiveArmProperties(
                    projection,
                    QueryProjectionKind.Member,
                    hasPath: false,
                    hasTypeName: true,
                    hasFields: true);
                break;
            case QueryProjectionKind.Construct:
                _ = ConstructTypeName(projection);
                RequireConstructFields(projection);
                RejectInactiveArmProperties(
                    projection,
                    QueryProjectionKind.Construct,
                    hasPath: true,
                    hasTypeName: false,
                    hasFields: false);
                break;
            default:
                throw new InvalidOperationException($"Unsupported QueryProjection kind '{projection.Kind}'.");
        }
    }

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

    private static void RequireConstructFields(QueryProjection projection)
    {
        for (var i = 0; i < projection.Fields.Count; i++)
        {
            if (projection.Fields[i] is null)
            {
                throw new InvalidOperationException(
                    $"QueryProjection Construct field at index {i} must not be null.");
            }
        }
    }

    private static void RejectInactiveArmProperties(
        QueryProjection projection,
        QueryProjectionKind kind,
        bool hasPath,
        bool hasTypeName,
        bool hasFields)
    {
        var inactive = new List<string>(3);
        if (hasPath && !string.IsNullOrEmpty(projection.Path))
        {
            inactive.Add(nameof(QueryProjection.Path));
        }

        if (hasTypeName && !string.IsNullOrEmpty(projection.TypeName))
        {
            inactive.Add(nameof(QueryProjection.TypeName));
        }

        if (hasFields && projection.Fields.Count > 0)
        {
            inactive.Add(nameof(QueryProjection.Fields));
        }

        if (inactive.Count > 0)
        {
            throw new InvalidOperationException(
                $"QueryProjection {kind} nodes cannot carry inactive union-arm properties: {string.Join(", ", inactive)}.");
        }
    }

    private static string NonEmpty(string? value, string message)
        => string.IsNullOrEmpty(value) ? throw new InvalidOperationException(message) : value;
}
