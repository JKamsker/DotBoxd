namespace DotBoxD.Queryable.Ast;

/// <summary>
/// The portable projection AST describing how a matched event is shaped before dispatch. A projection is
/// either the identity, a single dotted member read, or the construction of a DTO/anonymous payload from
/// member reads and constants. The model is descriptive: it lets a host inspect and serialize the shape
/// while the in-process runtime materializes values through the captured projection delegate.
/// </summary>
public sealed record QueryProjection
{
    private static readonly IReadOnlyList<QueryProjectionField> NoFields = [];

    /// <summary>The projection shape.</summary>
    public required QueryProjectionKind Kind { get; init; }

    /// <summary>The dotted member path for a <see cref="QueryProjectionKind.Member"/> projection.</summary>
    public string? Path { get; init; }

    /// <summary>The full name of the constructed type for a <see cref="QueryProjectionKind.Construct"/> projection.</summary>
    public string? TypeName { get; init; }

    /// <summary>The members of a <see cref="QueryProjectionKind.Construct"/> projection, in declaration order.</summary>
    public IReadOnlyList<QueryProjectionField> Fields { get; init; } = NoFields;

    /// <summary>The identity projection (the event flows through unchanged).</summary>
    public static QueryProjection Identity { get; } = new() { Kind = QueryProjectionKind.Identity };

    /// <summary>Builds a single-member projection.</summary>
    public static QueryProjection Member(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new QueryProjection { Kind = QueryProjectionKind.Member, Path = path };
    }

    /// <summary>Builds a DTO/anonymous-construction projection.</summary>
    public static QueryProjection Construct(string typeName, IReadOnlyList<QueryProjectionField> fields)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(fields);
        return new QueryProjection
        {
            Kind = QueryProjectionKind.Construct,
            TypeName = typeName,
            // Snapshot so a mutable list passed/cast by the caller cannot mutate the AST after construction.
            Fields = [.. fields],
        };
    }
}
