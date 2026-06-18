using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Text;

/// <summary>
/// A small, portable text DSL for the filter AST — a human-authorable and non-.NET-client-friendly form
/// that round-trips with <see cref="QueryFilter"/>. Grammar (precedence low→high): <c>or</c> (<c>or</c>/
/// <c>||</c>), <c>and</c> (<c>and</c>/<c>&amp;&amp;</c>), <c>not</c> (<c>not</c>/<c>!</c>), then a parenthesized
/// group or a leaf. A leaf is <c>path op value</c>, <c>path in [v, …]</c>, or <c>*</c> (match-all). An
/// operator may be prefixed with <c>~</c> for case-insensitive matching.
/// </summary>
public static class QueryText
{
    /// <summary>Formats a filter as DSL text.</summary>
    public static string Format(QueryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return QueryTextWriter.Write(filter);
    }

    /// <summary>Parses DSL text into a filter AST.</summary>
    public static QueryFilter Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return QueryTextParser.Parse(text);
    }

    internal static string OperatorToken(QueryComparisonOperator op) => op switch
    {
        QueryComparisonOperator.Equal => "==",
        QueryComparisonOperator.NotEqual => "!=",
        QueryComparisonOperator.GreaterThan => ">",
        QueryComparisonOperator.GreaterThanOrEqual => ">=",
        QueryComparisonOperator.LessThan => "<",
        QueryComparisonOperator.LessThanOrEqual => "<=",
        QueryComparisonOperator.StringContains => "contains",
        QueryComparisonOperator.StringStartsWith => "startswith",
        QueryComparisonOperator.StringEndsWith => "endswith",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown operator."),
    };

    internal static bool TryParseOperator(string token, out QueryComparisonOperator op)
    {
        op = token switch
        {
            "==" => QueryComparisonOperator.Equal,
            "!=" => QueryComparisonOperator.NotEqual,
            ">" => QueryComparisonOperator.GreaterThan,
            ">=" => QueryComparisonOperator.GreaterThanOrEqual,
            "<" => QueryComparisonOperator.LessThan,
            "<=" => QueryComparisonOperator.LessThanOrEqual,
            "contains" => QueryComparisonOperator.StringContains,
            "startswith" => QueryComparisonOperator.StringStartsWith,
            "endswith" => QueryComparisonOperator.StringEndsWith,
            _ => (QueryComparisonOperator)(-1),
        };

        return (int)op >= 0;
    }
}
