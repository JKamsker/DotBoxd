using System.Globalization;

namespace DotBoxD.Queryable.Ast;

/// <summary>
/// A portable, serializable literal captured from a query expression. Values are normalized into one of a
/// small set of <see cref="QueryValueKind"/> categories so the model can be transmitted, indexed, and
/// interpreted without depending on CLR type identities. <see cref="ParameterKey"/> records the capture
/// origin (a stable <c>p0</c>, <c>p1</c>… ordinal) for diagnostics and is not part of value equality.
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

    /// <summary>The value category.</summary>
    public QueryValueKind Kind { get; }

    /// <summary>The boolean payload when <see cref="Kind"/> is <see cref="QueryValueKind.Boolean"/>.</summary>
    public bool Boolean { get; }

    /// <summary>The integral payload when <see cref="Kind"/> is <see cref="QueryValueKind.Integer"/>.</summary>
    public long Integer { get; }

    /// <summary>The floating-point payload when <see cref="Kind"/> is <see cref="QueryValueKind.Number"/>.</summary>
    public double Number { get; }

    /// <summary>The string payload when <see cref="Kind"/> is <see cref="QueryValueKind.String"/>.</summary>
    public string? String { get; }

    /// <summary>
    /// A stable capture ordinal (<c>p0</c>, <c>p1</c>…) identifying where the literal originated in the
    /// authored expression. Provenance only; excluded from equality so two structurally identical values
    /// captured at different positions still compare equal.
    /// </summary>
    public string? ParameterKey { get; init; }

    /// <summary>The shared <c>null</c> literal.</summary>
    public static QueryValue Null { get; } = new(QueryValueKind.Null, false, 0, 0, null);

    /// <summary>Creates a boolean value.</summary>
    public static QueryValue FromBoolean(bool value) => new(QueryValueKind.Boolean, value, 0, 0, null);

    /// <summary>Creates an integral value.</summary>
    public static QueryValue FromInteger(long value) => new(QueryValueKind.Integer, false, value, 0, null);

    /// <summary>
    /// Creates a floating-point value. Non-finite values (NaN, ±Infinity) are rejected: the portable model
    /// (and its JSON wire form) cannot represent them, and they have no meaningful equality/ordering here.
    /// </summary>
    public static QueryValue FromNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("Non-finite numeric values (NaN, Infinity) are not supported.", nameof(value));
        }

        return new QueryValue(QueryValueKind.Number, false, 0, value, null);
    }

    /// <summary>Creates a string value; a <c>null</c> argument yields <see cref="Null"/>.</summary>
    public static QueryValue FromString(string? value) =>
        value is null ? Null : new(QueryValueKind.String, false, 0, 0, value);

    /// <summary>
    /// Normalizes a CLR object into a <see cref="QueryValue"/>. Supported inputs are <c>null</c>,
    /// <c>bool</c>, the integral types, <c>float</c>/<c>double</c>/<c>decimal</c>, <c>string</c>, and
    /// <c>enum</c> (carried by its integral value). Returns <see langword="false"/> for unsupported types.
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
                // ulong can exceed long.MaxValue; carry it as a double so the captured (expected) value and
                // the runtime (actual) value, which the comparer also widens to double, agree.
                result = FromNumber((double)u);
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
                result = FromNumber((double)m);
                return true;
            case Enum e:
                result = FromInteger(Convert.ToInt64(e, CultureInfo.InvariantCulture));
                return true;
            default:
                result = Null;
                return false;
        }
    }

    /// <summary>Renders a stable, culture-invariant textual form used for fingerprints and diagnostics.</summary>
    public string ToCanonicalText() => Kind switch
    {
        QueryValueKind.Null => "null",
        QueryValueKind.Boolean => Boolean ? "true" : "false",
        QueryValueKind.Integer => Integer.ToString(CultureInfo.InvariantCulture),
        QueryValueKind.Number => Number.ToString("R", CultureInfo.InvariantCulture),
        QueryValueKind.String => String ?? string.Empty,
        _ => string.Empty,
    };
}
