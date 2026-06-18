using System.Text;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Queryable.Text;

internal enum QueryTokenKind
{
    Word,
    String,
    Number,
    Symbol,
    End,
}

internal readonly record struct QueryToken(QueryTokenKind Kind, string Text);

/// <summary>Lexes the query text DSL into tokens; raises <see cref="QueryTranslationException"/> on bad input.</summary>
internal static class QueryTextTokenizer
{
    public static IReadOnlyList<QueryToken> Tokenize(string text)
    {
        var tokens = new List<QueryToken>();
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (c is '(' or ')' or '[' or ']' or ',' or '*' or '~')
            {
                tokens.Add(new QueryToken(QueryTokenKind.Symbol, c.ToString()));
                index++;
                continue;
            }

            if (TryLexOperator(text, ref index, out var op))
            {
                tokens.Add(new QueryToken(QueryTokenKind.Symbol, op));
                continue;
            }

            if (c == '"')
            {
                tokens.Add(new QueryToken(QueryTokenKind.String, LexString(text, ref index)));
                continue;
            }

            if (c == '-' || char.IsDigit(c))
            {
                tokens.Add(new QueryToken(QueryTokenKind.Number, LexNumber(text, ref index)));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                tokens.Add(new QueryToken(QueryTokenKind.Word, LexWord(text, ref index)));
                continue;
            }

            throw new QueryTranslationException($"Unexpected character '{c}' at position {index} in query text.");
        }

        tokens.Add(new QueryToken(QueryTokenKind.End, string.Empty));
        return tokens;
    }

    private static bool TryLexOperator(string text, ref int index, out string op)
    {
        var c = text[index];
        var next = index + 1 < text.Length ? text[index + 1] : '\0';
        switch (c)
        {
            case '&' when next == '&':
            case '|' when next == '|':
            case '=' when next == '=':
            case '!' when next == '=':
            case '>' when next == '=':
            case '<' when next == '=':
                op = text.Substring(index, 2);
                index += 2;
                return true;
            case '!' or '>' or '<':
                op = c.ToString();
                index++;
                return true;
            case '=':
                throw new QueryTranslationException($"Unexpected '=' at position {index}; use '==' for equality.");
            default:
                op = string.Empty;
                return false;
        }
    }

    private static string LexString(string text, ref int index)
    {
        var builder = new StringBuilder();
        index++; // opening quote
        while (index < text.Length)
        {
            var c = text[index++];
            if (c == '\\' && index < text.Length)
            {
                builder.Append(text[index++]);
                continue;
            }

            if (c == '"')
            {
                return builder.ToString();
            }

            builder.Append(c);
        }

        throw new QueryTranslationException("Unterminated string literal in query text.");
    }

    // Numbers: optional leading '-', digits/'.', and an optional well-formed exponent (e/E [+/-] digits) so
    // round-trip ("R") doubles such as 1E+21 or 1.5E-10 re-tokenize as a single Number.
    private static string LexNumber(string text, ref int index)
    {
        var start = index;
        if (text[index] == '-')
        {
            index++;
        }

        while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
        {
            index++;
        }

        if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
        {
            var exponent = index + 1;
            if (exponent < text.Length && (text[exponent] == '+' || text[exponent] == '-'))
            {
                exponent++;
            }

            if (exponent < text.Length && char.IsDigit(text[exponent]))
            {
                index = exponent + 1;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }
            }
        }

        return text[start..index];
    }

    private static string LexWord(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '.'))
        {
            index++;
        }

        return text[start..index];
    }
}
