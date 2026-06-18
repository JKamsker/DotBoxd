using System.Globalization;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Execution;

/// <summary>
/// Compares a runtime member value against a portable <see cref="QueryValue"/> for a given
/// <see cref="QueryComparisonOperator"/>. Numbers are compared numerically (integral and floating values
/// interoperate, enums by their underlying value), strings ordinally (optionally ignoring case), and
/// booleans by value. Incomparable operand pairs evaluate to <see langword="false"/> rather than throwing.
/// </summary>
public static class QueryValueComparer
{
    /// <summary>Evaluates <c>actual op expected</c>.</summary>
    public static bool Compare(object? actual, QueryComparisonOperator op, QueryValue expected, bool ignoreCase) => op switch
    {
        QueryComparisonOperator.Equal => AreEqual(actual, expected, ignoreCase),
        QueryComparisonOperator.NotEqual => !AreEqual(actual, expected, ignoreCase),
        QueryComparisonOperator.GreaterThan => Ordered(actual, expected) is { } c && c > 0,
        QueryComparisonOperator.GreaterThanOrEqual => Ordered(actual, expected) is { } c && c >= 0,
        QueryComparisonOperator.LessThan => Ordered(actual, expected) is { } c && c < 0,
        QueryComparisonOperator.LessThanOrEqual => Ordered(actual, expected) is { } c && c <= 0,
        QueryComparisonOperator.StringContains => StringMatch(actual, expected, ignoreCase, MatchMode.Contains),
        QueryComparisonOperator.StringStartsWith => StringMatch(actual, expected, ignoreCase, MatchMode.StartsWith),
        QueryComparisonOperator.StringEndsWith => StringMatch(actual, expected, ignoreCase, MatchMode.EndsWith),
        _ => false,
    };

    /// <summary>Returns <see langword="true"/> when <paramref name="actual"/> equals any of <paramref name="candidates"/>.</summary>
    public static bool IsAnyEqual(object? actual, IReadOnlyList<QueryValue> candidates, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (AreEqual(actual, candidates[i], ignoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines value equality between a runtime value and a portable value.</summary>
    public static bool AreEqual(object? actual, QueryValue expected, bool ignoreCase)
    {
        switch (expected.Kind)
        {
            case QueryValueKind.Null:
                return actual is null;
            case QueryValueKind.Boolean:
                return actual is bool b && b == expected.Boolean;
            case QueryValueKind.String:
                return actual is string s &&
                    string.Equals(s, expected.String, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            case QueryValueKind.Integer:
            case QueryValueKind.Number:
                return TryToDouble(actual, out var number) && number.Equals(ToDouble(expected));
            default:
                return false;
        }
    }

    private static int? Ordered(object? actual, QueryValue expected)
    {
        if (expected.Kind is QueryValueKind.Integer or QueryValueKind.Number)
        {
            return TryToDouble(actual, out var number) ? number.CompareTo(ToDouble(expected)) : null;
        }

        if (expected.Kind == QueryValueKind.String && actual is string s)
        {
            return string.CompareOrdinal(s, expected.String);
        }

        return null;
    }

    private static bool StringMatch(object? actual, QueryValue expected, bool ignoreCase, MatchMode mode)
    {
        if (actual is not string s || expected.String is not { } needle)
        {
            return false;
        }

        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return mode switch
        {
            MatchMode.Contains => s.Contains(needle, comparison),
            MatchMode.StartsWith => s.StartsWith(needle, comparison),
            MatchMode.EndsWith => s.EndsWith(needle, comparison),
            _ => false,
        };
    }

    private static double ToDouble(QueryValue value)
        => value.Kind == QueryValueKind.Integer ? value.Integer : value.Number;

    private static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case bool:
            case string:
                result = 0;
                return false;
            case Enum e:
                result = Convert.ToInt64(e, CultureInfo.InvariantCulture);
                return true;
            default:
                try
                {
                    result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
                {
                    result = 0;
                    return false;
                }
        }
    }

    private enum MatchMode
    {
        Contains,
        StartsWith,
        EndsWith,
    }
}
