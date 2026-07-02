using System.Globalization;
using System.Text;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Authoring;

/// <summary>
/// A normalized equality key used to route an event to candidate subscriptions without scanning every
/// subscription. A key pairs a member path with a scalar value; the same key is produced from a
/// subscription's equality predicate and from a runtime event's member value, so dictionary lookup yields
/// only the subscriptions whose indexed equality the event satisfies. This index is independent of the host
/// index vocabulary, so the exact kinds (Guid/Decimal/UnsignedInteger/Timestamp) are equality-routable here
/// even though they are not host-indexable.
/// </summary>
internal readonly record struct EventQueryRoutingKey(
    string Path,
    QueryValueKind Kind,
    long Integer,
    double Number,
    bool Boolean,
    string? Text,
    Guid Guid,
    decimal Decimal,
    ulong UnsignedInteger,
    long Ticks)
{
    /// <summary>
    /// Builds a routing key from a subscription's equality bound. Integer and floating values collapse to a
    /// single numeric (<see cref="QueryValueKind.Number"/>) form keyed on the <see cref="double"/> value, so
    /// a whole-number literal (<c>e.Score == 100</c>) routes to a floating member read as <c>100.0</c> — a
    /// harmless collision since the full filter still runs on candidates. The exact kinds keep their own
    /// distinct buckets so a <c>ulong</c> &gt; <see cref="long.MaxValue"/> never aliases a signed long and a
    /// scale-varying decimal still routes consistently.
    /// </summary>
    public static EventQueryRoutingKey FromValue(string path, QueryValue value) => value.Kind switch
    {
        QueryValueKind.Boolean => new(path, value.Kind, 0, 0, value.Boolean, null, default, 0m, 0, 0),
        QueryValueKind.Integer => new(path, QueryValueKind.Number, 0, value.Integer, false, null, default, 0m, 0, 0),
        QueryValueKind.Number => new(path, QueryValueKind.Number, 0, value.Number, false, null, default, 0m, 0, 0),
        QueryValueKind.String => new(path, value.Kind, 0, 0, false, value.String, default, 0m, 0, 0),
        QueryValueKind.Guid => new(path, value.Kind, 0, 0, false, null, value.Guid, 0m, 0, 0),
        QueryValueKind.Decimal => new(path, value.Kind, 0, 0, false, null, default, value.Decimal, 0, 0),
        QueryValueKind.UnsignedInteger => new(path, value.Kind, 0, 0, false, null, default, 0m, value.UnsignedInteger, 0),
        QueryValueKind.Timestamp => new(path, value.Kind, 0, 0, false, null, default, 0m, 0, value.Timestamp.UtcTicks),
        _ => new(path, QueryValueKind.Null, 0, 0, false, null, default, 0m, 0, 0),
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
    /// where the path order is implied by position. Each kind uses a distinct prefix so different kinds never
    /// alias, and the exact kinds use their canonical (scale-normalized / instant) form so the route agrees
    /// with the comparer's equality.
    /// </summary>
    public string ValueToken()
    {
        var builder = new StringBuilder(24);
        AppendValueToken(builder);
        return builder.ToString();
    }

    /// <summary>
    /// Appends this key's value token to <paramref name="builder"/> without the per-path intermediate string
    /// that <see cref="ValueToken"/> would allocate. Scalar values span-format directly into the builder; the
    /// output is byte-identical to <see cref="ValueToken"/> so composite keys built at registration and at
    /// runtime still agree. This is the single source of truth — <see cref="ValueToken"/> delegates here.
    /// </summary>
    public void AppendValueToken(StringBuilder builder)
    {
        switch (Kind)
        {
            case QueryValueKind.Boolean:
                builder.Append(Boolean ? "B1" : "B0");
                return;
            case QueryValueKind.Number:
                // Canonicalize signed zero (-0.0 and 0.0 compare equal) so a member holding -0.0 still routes
                // to a `== 0.0` subscription, keeping the routing token consistent with the comparer's equality.
                builder.Append('N');
                AppendFormatted(builder, Number == 0.0 ? 0.0 : Number, "R");
                return;
            case QueryValueKind.String:
                builder.Append('S').Append(Text);
                return;
            case QueryValueKind.Guid:
                builder.Append('G');
                AppendGuidN(builder, Guid);
                return;
            case QueryValueKind.Decimal:
                // Decimal has no span format that yields the scale-normalized canonical form, so keep the string.
                builder.Append('M').Append(QueryValue.CanonicalDecimal(Decimal));
                return;
            case QueryValueKind.UnsignedInteger:
                builder.Append('U');
                AppendFormatted(builder, UnsignedInteger);
                return;
            case QueryValueKind.Timestamp:
                builder.Append('T');
                AppendFormatted(builder, Ticks);
                return;
            default:
                builder.Append('X');
                return;
        }
    }

    private static void AppendFormatted(StringBuilder builder, double value, string format)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
        {
            builder.Append(buffer[..written]);
            return;
        }

        builder.Append(value.ToString(format, CultureInfo.InvariantCulture));
    }

    private static void AppendFormatted(StringBuilder builder, ulong value)
    {
        Span<char> buffer = stackalloc char[20];
        if (value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
        {
            builder.Append(buffer[..written]);
            return;
        }

        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendFormatted(StringBuilder builder, long value)
    {
        Span<char> buffer = stackalloc char[20];
        if (value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
        {
            builder.Append(buffer[..written]);
            return;
        }

        builder.Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendGuidN(StringBuilder builder, Guid value)
    {
        Span<char> buffer = stackalloc char[32];
        if (value.TryFormat(buffer, out var written, "N"))
        {
            builder.Append(buffer[..written]);
            return;
        }

        builder.Append(value.ToString("N"));
    }
}
