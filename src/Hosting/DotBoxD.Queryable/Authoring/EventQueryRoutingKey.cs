using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// A normalized equality key used to route an event to candidate subscriptions without scanning every
/// subscription. A key pairs a member path with a scalar value; the same key is produced from a
/// subscription's equality predicate and from a runtime event's member value, so dictionary lookup yields
/// only the subscriptions whose indexed equality the event satisfies.
/// </summary>
internal readonly record struct EventQueryRoutingKey(
    string Path,
    QueryValueKind Kind,
    long Integer,
    double Number,
    bool Boolean,
    string? Text)
{
    /// <summary>
    /// Builds a routing key from a subscription's equality bound. Integer and floating values collapse to a
    /// single numeric (<see cref="QueryValueKind.Number"/>) form keyed on the <see cref="double"/> value, so
    /// a whole-number literal (<c>e.Score == 100</c>) routes to a floating member read as <c>100.0</c>. A
    /// numeric collision is harmless: the full filter still runs on candidates.
    /// </summary>
    public static EventQueryRoutingKey FromValue(string path, QueryValue value) => value.Kind switch
    {
        QueryValueKind.Boolean => new(path, value.Kind, 0, 0, value.Boolean, null),
        QueryValueKind.Integer => new(path, QueryValueKind.Number, 0, value.Integer, false, null),
        QueryValueKind.Number => new(path, QueryValueKind.Number, 0, value.Number, false, null),
        QueryValueKind.String => new(path, value.Kind, 0, 0, false, value.String),
        _ => new(path, QueryValueKind.Null, 0, 0, false, null),
    };

    /// <summary>
    /// Builds a routing key from a runtime member value. Returns <see langword="false"/> for values that
    /// cannot form an equality key (null or unsupported types), which simply means no indexed match.
    /// </summary>
    public static bool TryFromRuntime(string path, object? runtime, out EventQueryRoutingKey key)
    {
        if (QueryValue.TryFromObject(runtime, out var value) && value.Kind != QueryValueKind.Null)
        {
            key = FromValue(path, value);
            return true;
        }

        key = default;
        return false;
    }

    /// <summary>
    /// A path-independent token for the key's value, used to build composite (multi-equality) routing keys
    /// where the path order is implied by position.
    /// </summary>
    public string ValueToken() => Kind switch
    {
        QueryValueKind.Boolean => Boolean ? "B1" : "B0",
        // Canonicalize signed zero (-0.0 and 0.0 compare equal) so a member holding -0.0 still routes to a
        // `== 0.0` subscription, keeping the routing token consistent with the comparer's equality.
        QueryValueKind.Number => "N" + (Number == 0.0 ? 0.0 : Number).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        QueryValueKind.String => "S" + Text,
        _ => "X",
    };
}
