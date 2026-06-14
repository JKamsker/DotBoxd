using System.Text;
using System.Text.Json;

namespace DotBoxd.Kernels.Serialization.Json.Internal;

internal sealed class JsonSourceMap
{
    private readonly Dictionary<JsonElement, SourceSpan> _spansByElement;

    private JsonSourceMap(Dictionary<JsonElement, SourceSpan> spansByElement)
        => _spansByElement = spansByElement;

    public static JsonSourceMap Create(string json, JsonElement root)
    {
        var tokenSpans = JsonTokenSpans.Read(json);
        var spansByElement = new Dictionary<JsonElement, SourceSpan>();
        var index = 0;
        Visit(root, tokenSpans, spansByElement, ref index);
        return new JsonSourceMap(spansByElement);
    }

    public SourceSpan SpanFor(JsonElement element)
        => _spansByElement.TryGetValue(element, out var span) ? span : JsonImport.JsonSpan;

    private static void Visit(
        JsonElement element,
        IReadOnlyList<SourceSpan> tokenSpans,
        Dictionary<JsonElement, SourceSpan> spansByElement,
        ref int index)
    {
        spansByElement[element] = index < tokenSpans.Count ? tokenSpans[index++] : JsonImport.JsonSpan;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value, tokenSpans, spansByElement, ref index);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, tokenSpans, spansByElement, ref index);
                }

                break;
        }
    }

    private static class JsonTokenSpans
    {
        public static IReadOnlyList<SourceSpan> Read(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            var positions = new SourcePositionTracker(json);
            var spans = new List<SourceSpan>();
            while (reader.Read())
            {
                if (IsValueToken(reader.TokenType))
                {
                    spans.Add(positions.SpanAt(reader.TokenStartIndex));
                }
            }

            return spans;
        }

        private static bool IsValueToken(JsonTokenType tokenType)
            => tokenType is JsonTokenType.StartObject
                or JsonTokenType.StartArray
                or JsonTokenType.String
                or JsonTokenType.Number
                or JsonTokenType.True
                or JsonTokenType.False
                or JsonTokenType.Null;
    }

    private sealed class SourcePositionTracker
    {
        private readonly string _json;
        private int _charIndex;
        private long _byteOffset;
        private int _line = 1;
        private int _column = 1;

        public SourcePositionTracker(string json)
            => _json = json;

        public SourceSpan SpanAt(long byteOffset)
        {
            while (_charIndex < _json.Length && _byteOffset < byteOffset)
            {
                Advance();
            }

            return new SourceSpan(_line, _column);
        }

        private void Advance()
        {
            var current = _json[_charIndex];
            if (current == '\r')
            {
                AdvanceNewLine(charCount: IsCrLf() ? 2 : 1);
                return;
            }

            if (current == '\n')
            {
                AdvanceNewLine(charCount: 1);
                return;
            }

            var charCount = IsSurrogatePair() ? 2 : 1;
            _byteOffset += Encoding.UTF8.GetByteCount(_json.AsSpan(_charIndex, charCount));
            _charIndex += charCount;
            _column += charCount;
        }

        private void AdvanceNewLine(int charCount)
        {
            _byteOffset += charCount;
            _charIndex += charCount;
            _line++;
            _column = 1;
        }

        private bool IsCrLf()
            => _charIndex + 1 < _json.Length && _json[_charIndex + 1] == '\n';

        private bool IsSurrogatePair()
            => char.IsHighSurrogate(_json[_charIndex]) &&
                _charIndex + 1 < _json.Length &&
                char.IsLowSurrogate(_json[_charIndex + 1]);
    }
}
