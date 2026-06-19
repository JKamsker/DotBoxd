using System.Globalization;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Queryable.Text;

/// <summary>Recursive-descent parser for the query text DSL (see <see cref="QueryText"/>).</summary>
internal static class QueryTextParser
{
    // Bounds parser recursion so deeply nested text (e.g. thousands of '(' or 'not') raises a catchable
    // QueryTranslationException instead of an uncatchable StackOverflowException. The runtime evaluator is
    // separately bounded by QueryEvaluationLimits once an AST exists.
    private const int MaxParseDepth = 256;

    public static QueryFilter Parse(string text)
    {
        var cursor = new Cursor(QueryTextTokenizer.Tokenize(text));
        var filter = ParseOr(cursor, depth: 0);
        cursor.ExpectEnd();
        return filter;
    }

    private static QueryFilter ParseOr(Cursor cursor, int depth)
    {
        EnsureDepth(depth);
        var terms = new List<QueryFilter> { ParseAnd(cursor, depth + 1) };
        while (cursor.TryConsumeKeywordOrSymbol("or", "||"))
        {
            terms.Add(ParseAnd(cursor, depth + 1));
        }

        return terms.Count == 1 ? terms[0] : QueryFilter.Or(terms);
    }

    private static QueryFilter ParseAnd(Cursor cursor, int depth)
    {
        EnsureDepth(depth);
        var terms = new List<QueryFilter> { ParseUnary(cursor, depth + 1) };
        while (cursor.TryConsumeKeywordOrSymbol("and", "&&"))
        {
            terms.Add(ParseUnary(cursor, depth + 1));
        }

        return terms.Count == 1 ? terms[0] : QueryFilter.And(terms);
    }

    private static QueryFilter ParseUnary(Cursor cursor, int depth)
    {
        EnsureDepth(depth);
        return cursor.TryConsumeKeywordOrSymbol("not", "!")
            ? QueryFilter.Not(ParseUnary(cursor, depth + 1))
            : ParsePrimary(cursor, depth + 1);
    }

    private static QueryFilter ParsePrimary(Cursor cursor, int depth)
    {
        EnsureDepth(depth);
        if (cursor.TryConsumeSymbol("("))
        {
            var filter = ParseOr(cursor, depth + 1);
            cursor.ExpectSymbol(")");
            return filter;
        }

        return cursor.TryConsumeSymbol("*") ? QueryFilter.MatchAll : ParseLeaf(cursor);
    }

    private static void EnsureDepth(int depth)
    {
        if (depth > MaxParseDepth)
        {
            throw new QueryTranslationException($"Query text nesting exceeds the maximum depth of {MaxParseDepth}.");
        }
    }

    private static QueryFilter ParseLeaf(Cursor cursor)
    {
        var path = cursor.ExpectWord();
        var ignoreCase = cursor.TryConsumeSymbol("~");

        if (cursor.TryConsumeKeyword("in"))
        {
            return ParseIn(cursor, path, ignoreCase);
        }

        var token = cursor.ExpectOperatorToken();
        if (!QueryText.TryParseOperator(token, out var op))
        {
            throw new QueryTranslationException($"Unknown operator '{token}' in query text.");
        }

        return QueryFilter.Compare(path, op, ParseValue(cursor), ignoreCase);
    }

    private static QueryFilter ParseIn(Cursor cursor, string path, bool ignoreCase)
    {
        cursor.ExpectSymbol("[");
        var values = new List<QueryValue>();
        if (!cursor.IsSymbol("]"))
        {
            values.Add(ParseValue(cursor));
            while (cursor.TryConsumeSymbol(","))
            {
                values.Add(ParseValue(cursor));
            }
        }

        cursor.ExpectSymbol("]");
        return QueryFilter.In(path, values, ignoreCase);
    }

    private static QueryValue ParseValue(Cursor cursor)
    {
        var token = cursor.Current;
        switch (token.Kind)
        {
            case QueryTokenKind.String:
                cursor.Advance();
                return QueryValue.FromString(token.Text);
            case QueryTokenKind.Number:
                cursor.Advance();
                return ParseNumber(token.Text);
            case QueryTokenKind.Word when token.Text is "guid" or "ts":
                return ParseTaggedValue(cursor, token.Text);
            case QueryTokenKind.Word:
                cursor.Advance();
                return token.Text switch
                {
                    "true" => QueryValue.FromBoolean(true),
                    "false" => QueryValue.FromBoolean(false),
                    "null" => QueryValue.Null,
                    _ => throw new QueryTranslationException($"Expected a value but found '{token.Text}'."),
                };
            default:
                throw new QueryTranslationException("Expected a value in query text.");
        }
    }

    private static QueryValue ParseNumber(string raw)
    {
        if (raw.Length > 1 && raw[^1] is 'm' or 'M')
        {
            return decimal.TryParse(raw[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var dec)
                ? QueryValue.FromDecimal(dec)
                : throw new QueryTranslationException($"Invalid decimal '{raw}' in query text.");
        }

        if (raw.Length > 1 && raw[^1] is 'u' or 'U')
        {
            return ulong.TryParse(raw[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsigned)
                ? QueryValue.FromUnsignedInteger(unsigned)
                : throw new QueryTranslationException($"Invalid unsigned integer '{raw}' in query text.");
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return QueryValue.FromInteger(integer);
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return QueryValue.FromNumber(number);
        }

        throw new QueryTranslationException($"Invalid number '{raw}' in query text.");
    }

    // Tag-prefixed value literals: guid("…") and ts("…"). The quoted form keeps the value's '-'/':' from
    // tripping the number/operator lexers, and parsing always normalizes a timestamp to a UTC instant.
    private static QueryValue ParseTaggedValue(Cursor cursor, string tag)
    {
        cursor.Advance(); // the tag word (guid/ts)
        cursor.ExpectSymbol("(");
        var inner = cursor.Current;
        if (inner.Kind != QueryTokenKind.String)
        {
            throw new QueryTranslationException($"Expected a quoted value after '{tag}('.");
        }

        cursor.Advance();
        cursor.ExpectSymbol(")");
        return tag switch
        {
            "guid" => Guid.TryParse(inner.Text, out var g)
                ? QueryValue.FromGuid(g)
                : throw new QueryTranslationException($"Invalid guid '{inner.Text}' in query text."),
            "ts" => QueryValue.HasExplicitTimestampOffset(inner.Text)
                    && DateTimeOffset.TryParse(inner.Text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                ? QueryValue.FromTimestamp(dto)
                : throw new QueryTranslationException($"Invalid or offset-less timestamp '{inner.Text}' in query text."),
            _ => throw new QueryTranslationException($"Unknown value tag '{tag}'."),
        };
    }

    private sealed class Cursor(IReadOnlyList<QueryToken> tokens)
    {
        private int _position;

        public QueryToken Current => tokens[_position];

        public void Advance() => _position++;

        public bool IsSymbol(string symbol) => Current is { Kind: QueryTokenKind.Symbol } t && t.Text == symbol;

        public bool TryConsumeSymbol(string symbol)
        {
            if (IsSymbol(symbol))
            {
                Advance();
                return true;
            }

            return false;
        }

        public bool TryConsumeKeyword(string keyword)
        {
            if (Current is { Kind: QueryTokenKind.Word } t && t.Text == keyword)
            {
                Advance();
                return true;
            }

            return false;
        }

        public bool TryConsumeKeywordOrSymbol(string keyword, string symbol)
            => TryConsumeKeyword(keyword) || TryConsumeSymbol(symbol);

        public void ExpectSymbol(string symbol)
        {
            if (!TryConsumeSymbol(symbol))
            {
                throw new QueryTranslationException($"Expected '{symbol}' but found '{Describe(Current)}'.");
            }
        }

        public void ExpectEnd()
        {
            if (Current.Kind != QueryTokenKind.End)
            {
                throw new QueryTranslationException($"Unexpected trailing input '{Describe(Current)}' in query text.");
            }
        }

        public string ExpectWord()
        {
            if (Current.Kind != QueryTokenKind.Word)
            {
                throw new QueryTranslationException($"Expected a field name but found '{Describe(Current)}'.");
            }

            var text = Current.Text;
            Advance();
            return text;
        }

        public string ExpectOperatorToken()
        {
            if (Current.Kind is QueryTokenKind.Symbol or QueryTokenKind.Word)
            {
                var text = Current.Text;
                Advance();
                return text;
            }

            throw new QueryTranslationException($"Expected an operator but found '{Describe(Current)}'.");
        }

        private static string Describe(QueryToken token) => token.Kind == QueryTokenKind.End ? "<end>" : token.Text;
    }
}
