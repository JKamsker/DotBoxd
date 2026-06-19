using System.Globalization;

namespace DotBoxD.Queryable.Ast;

/// <summary>
/// A portable, serializable literal captured from a query expression. Values are normalized into one of a
/// small set of <see cref="QueryValueKind"/> categories so the model can be transmitted, indexed, and
/// interpreted without depending on CLR type identities. The integral and floating kinds stay exact
/// (<c>long</c>, <c>ulong</c>, <c>decimal</c> never collapse to <c>double</c>); only genuine <c>float</c>/
/// <c>double</c> values use the inexact <see cref="QueryValueKind.Number"/> kind. <see cref="ParameterKey"/>
/// records the capture origin (a stable <c>p0</c>, <c>p1</c>… ordinal) and is not part of value equality.
/// </summary>
public sealed record QueryValue
{
    private QueryValue(QueryValueKind kind, bool boolean, long integer, double number, string? text)
    {
        Kind = kind;
        Boolean = boolean;
        Integer = integer;
        Number = number;
        String = text;
    }

    private QueryValue(QueryValueKind kind, Guid guid)
    {
        Kind = kind;
        Guid = guid;
    }

    private QueryValue(QueryValueKind kind, decimal value)
    {
        Kind = kind;
        Decimal = value;
    }

    private QueryValue(QueryValueKind kind, ulong value)
    {
        Kind = kind;
        UnsignedInteger = value;
    }

    private QueryValue(QueryValueKind kind, DateTimeOffset value)
    {
        Kind = kind;
        Timestamp = value;
    }

    /// <summary>The value category.</summary>
    public QueryValueKind Kind { get; }

    /// <summary>The boolean payload when <see cref="Kind"/> is <see cref="QueryValueKind.Boolean"/>.</summary>
    public bool Boolean { get; }

    /// <summary>The signed integral payload when <see cref="Kind"/> is <see cref="QueryValueKind.Integer"/>.</summary>
    public long Integer { get; }

    /// <summary>The floating-point payload when <see cref="Kind"/> is <see cref="QueryValueKind.Number"/>.</summary>
    public double Number { get; }

    /// <summary>The string payload when <see cref="Kind"/> is <see cref="QueryValueKind.String"/>.</summary>
    public string? String { get; }

    /// <summary>The GUID payload when <see cref="Kind"/> is <see cref="QueryValueKind.Guid"/>.</summary>
    public Guid Guid { get; }

    /// <summary>The exact decimal payload when <see cref="Kind"/> is <see cref="QueryValueKind.Decimal"/>.</summary>
    public decimal Decimal { get; }

    /// <summary>The exact unsigned integral payload when <see cref="Kind"/> is <see cref="QueryValueKind.UnsignedInteger"/>.</summary>
    public ulong UnsignedInteger { get; }

    /// <summary>The UTC-normalized instant when <see cref="Kind"/> is <see cref="QueryValueKind.Timestamp"/>.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// A stable capture ordinal (<c>p0</c>, <c>p1</c>…) identifying where the literal originated in the
    /// authored expression. Provenance only; excluded from equality so two structurally identical values
    /// captured at different positions still compare equal.
    /// </summary>
    public string? ParameterKey { get; init; }

    /// <summary>
    /// Value equality covers only the value payload (<see cref="Kind"/> and its scalar fields). The provenance
    /// <see cref="ParameterKey"/> is deliberately excluded so two structurally identical literals captured at
    /// different positions compare equal and hash alike. Decimal equality is value-based (scale-insensitive),
    /// and timestamps compare by their UTC instant.
    /// </summary>
    public bool Equals(QueryValue? other) =>
        other is not null
        && Kind == other.Kind
        && Boolean == other.Boolean
        && Integer == other.Integer
        && Number == other.Number
        && String == other.String
        && Guid == other.Guid
        && Decimal == other.Decimal
        && UnsignedInteger == other.UnsignedInteger
        && Timestamp.UtcTicks == other.Timestamp.UtcTicks;

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Boolean);
        hash.Add(Integer);
        hash.Add(Number);
        hash.Add(String);
        hash.Add(Guid);
        hash.Add(Decimal);
        hash.Add(UnsignedInteger);
        hash.Add(Timestamp.UtcTicks);
        return hash.ToHashCode();
    }

    /// <summary>The shared <c>null</c> literal.</summary>
    public static QueryValue Null { get; } = new(QueryValueKind.Null, false, 0, 0, null);

    /// <summary>Creates a boolean value.</summary>
    public static QueryValue FromBoolean(bool value) => new(QueryValueKind.Boolean, value, 0, 0, null);

    /// <summary>Creates a signed integral value.</summary>
    public static QueryValue FromInteger(long value) => new(QueryValueKind.Integer, false, value, 0, null);

    /// <summary>
    /// Creates a floating-point value. Non-finite values (NaN, ±Infinity) are rejected: the portable model
    /// (and its JSON wire form) cannot represent them, and they have no meaningful equality/ordering here.
    /// Negative zero is normalized to positive zero so <c>-0.0</c> and <c>0.0</c> are indistinguishable.
    /// </summary>
    public static QueryValue FromNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("Non-finite numeric values (NaN, Infinity) are not supported.", nameof(value));
        }

        if (value == 0.0)
        {
            value = 0.0; // collapse -0.0 to +0.0 so fingerprints/equality/hash agree
        }

        return new QueryValue(QueryValueKind.Number, false, 0, value, null);
    }

    /// <summary>Creates a string value; a <c>null</c> argument yields <see cref="Null"/>.</summary>
    public static QueryValue FromString(string? value) =>
        value is null ? Null : new(QueryValueKind.String, false, 0, 0, value);

    /// <summary>Creates a <see cref="System.Guid"/> value.</summary>
    public static QueryValue FromGuid(Guid value) => new(QueryValueKind.Guid, value);

    /// <summary>Creates an exact decimal value. Equality is scale-insensitive (<c>1.10m</c> equals <c>1.100m</c>).</summary>
    public static QueryValue FromDecimal(decimal value) => new(QueryValueKind.Decimal, value);

    /// <summary>Creates an exact unsigned integral value (full <c>ulong</c> range, no precision loss).</summary>
    public static QueryValue FromUnsignedInteger(ulong value) => new(QueryValueKind.UnsignedInteger, value);

    /// <summary>Creates a timestamp value, normalizing the input to a UTC instant.</summary>
    public static QueryValue FromTimestamp(DateTimeOffset value) => new(QueryValueKind.Timestamp, value.ToUniversalTime());

    /// <summary>
    /// Normalizes a CLR object into a <see cref="QueryValue"/>. Supported inputs are <c>null</c>, <c>bool</c>,
    /// the integral types (<c>ulong</c> kept exact), <c>float</c>/<c>double</c>, <c>decimal</c> (kept exact),
    /// <c>string</c>, <c>enum</c> (by its integral value), <see cref="System.Guid"/>, and the date/time types
    /// (<c>DateTime</c>/<c>DateTimeOffset</c>/<c>DateOnly</c>, normalized to a UTC instant). Returns
    /// <see langword="false"/> for unsupported types.
    /// </summary>
    public static bool TryFromObject(object? value, out QueryValue result)
    {
        switch (value)
        {
            case null:
                result = Null;
                return true;
            case bool b:
                result = FromBoolean(b);
                return true;
            case string s:
                result = FromString(s);
                return true;
            case sbyte or byte or short or ushort or int or uint or long:
                result = FromInteger(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return true;
            case ulong u:
                result = FromUnsignedInteger(u);
                return true;
            case float or double:
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (!double.IsFinite(number))
                {
                    result = Null;
                    return false;
                }

                result = FromNumber(number);
                return true;
            case decimal m:
                result = FromDecimal(m);
                return true;
            case Guid g:
                result = FromGuid(g);
                return true;
            case DateTimeOffset dto:
                result = FromTimestamp(dto);
                return true;
            case DateTime dt:
                result = FromTimestamp(ToOffset(dt));
                return true;
            case DateOnly d:
                result = FromTimestamp(new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
                return true;
            case Enum e:
                // Carry a ulong-backed enum exactly (its value may exceed long.MaxValue); other enums are signed.
                result = Enum.GetUnderlyingType(e.GetType()) == typeof(ulong)
                    ? FromUnsignedInteger(Convert.ToUInt64(e, CultureInfo.InvariantCulture))
                    : FromInteger(Convert.ToInt64(e, CultureInfo.InvariantCulture));
                return true;
            default:
                result = Null;
                return false;
        }
    }

    /// <summary>Renders a stable, culture-invariant textual form used for In-list sort ordering and diagnostics.</summary>
    public string ToCanonicalText() => Kind switch
    {
        QueryValueKind.Null => "null",
        QueryValueKind.Boolean => Boolean ? "true" : "false",
        QueryValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
        QueryValueKind.Number => Number.ToString("R", CultureInfo.InvariantCulture),
        QueryValueKind.String => String ?? string.Empty,
        QueryValueKind.Guid => Guid.ToString("D"),
        QueryValueKind.Decimal => CanonicalDecimal(Decimal),
        QueryValueKind.UnsignedInteger => UnsignedInteger.ToString(CultureInfo.InvariantCulture),
        QueryValueKind.Timestamp => CanonicalTimestamp(Timestamp),
        _ => string.Empty,
    };

    /// <summary>
    /// The scale-insensitive canonical text of a decimal (trailing zeros stripped) so <c>1.10m</c> and
    /// <c>1.100m</c> produce the same wire/fingerprint/routing form. Decimal text is always fixed-point.
    /// </summary>
    internal static string CanonicalDecimal(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (text.IndexOf('.') < 0)
        {
            return text;
        }

        text = text.TrimEnd('0');
        return text.EndsWith('.') ? text[..^1] : text;
    }

    /// <summary>The canonical UTC ISO-8601 round-trip text of a timestamp instant.</summary>
    internal static string CanonicalTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether a timestamp string carries an explicit offset — a trailing <c>Z</c> or a <c>±hh:mm</c> suffix.
    /// Parsers reject offset-less strings: <see cref="DateTimeOffset"/> parsing would otherwise default a
    /// missing offset to the host's local time zone, making the captured instant non-deterministic across
    /// machines. The canonical form this type emits always includes <c>Z</c>, so only malformed input is rejected.
    /// </summary>
    internal static bool HasExplicitTimestampOffset(string text)
    {
        if (text.EndsWith('Z') || text.EndsWith('z'))
        {
            return true;
        }

        if (text.Length >= 6)
        {
            var tail = text.AsSpan(text.Length - 6);
            if ((tail[0] == '+' || tail[0] == '-') &&
                char.IsAsciiDigit(tail[1]) && char.IsAsciiDigit(tail[2]) &&
                tail[3] == ':' &&
                char.IsAsciiDigit(tail[4]) && char.IsAsciiDigit(tail[5]))
            {
                return true;
            }
        }

        return false;
    }

    private static DateTimeOffset ToOffset(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
        DateTimeKind.Local => new DateTimeOffset(value),
        // Unspecified: assume UTC so the captured instant is deterministic across machines.
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero),
    };
}
