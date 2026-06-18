using System.Globalization;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Queryable.Text;

/// <summary>Recursive-descent parser for the query text DSL (see <see cref="QueryText"/>).</summary>
internal static class QueryTextParser
{
    public static QueryFilter Parse(string text)
    {
        var cursor = new Cursor(QueryTextTokenizer.Tokenize(text));
        var filter = ParseOr(cursor);
        cursor.ExpectEnd();
        return filter;
    }

    private static QueryFilter ParseOr(Cursor cursor)
    {
        var terms = new List<QueryFilter> { ParseAnd(cursor) };
        while (cursor.TryConsumeKeywordOrSymbol("or", "||"))
        {
            terms.Add(ParseAnd(cursor));
        }

        return terms.Count == 1 ? terms[0] : QueryFilter.Or(terms);
    }

    private static QueryFilter ParseAnd(Cursor cursor)
    {
        var terms = new List<QueryFilter> { ParseUnary(cursor) };
        while (cursor.TryConsumeKeywordOrSymbol("and", "&&"))
        {
            terms.Add(ParseUnary(cursor));
        }

        return terms.Count == 1 ? terms[0] : QueryFilter.And(terms);
    }

    private static QueryFilter ParseUnary(Cursor cursor)
        => cursor.TryConsumeKeywordOrSymbol("not", "!")
            ? QueryFilter.Not(ParseUnary(cursor))
            : ParsePrimary(cursor);

    private static QueryFilter ParsePrimary(Cursor cursor)
    {
        if (cursor.TryConsumeSymbol("("))
        {
            var filter = ParseOr(cursor);
            cursor.ExpectSymbol(")");
            return filter;
        }

        return cursor.TryConsumeSymbol("*") ? QueryFilter.MatchAll : ParseLeaf(cursor);
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
