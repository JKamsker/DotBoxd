using System.Text;
using System.Text.Json;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Serialization.Json.Internal;

internal sealed class JsonSourceMap
{
    private readonly JsonElement _root;
    private readonly IReadOnlyList<SourceSpan> _tokenSpans;
    private readonly Dictionary<JsonElement, SourceSpan> _spansByElement;

    private JsonSourceMap(
        JsonElement root,
        IReadOnlyList<SourceSpan> tokenSpans,
        Dictionary<JsonElement, SourceSpan> spansByElement)
    {
        _root = root;
        _tokenSpans = tokenSpans;
        _spansByElement = spansByElement;
    }

    public static JsonSourceMap Create(string json, JsonElement root)
    {
        var tokenSpans = JsonTokenSpans.Read(json);
        // JsonElement equality is document/index identity, which is exactly what this map needs.
        // Its default hash is value-oriented and can walk large subtrees, so use a non-value hash
        // and let the identity equality check disambiguate the small per-document span table.
#pragma warning disable MA0066
        var spansByElement = new Dictionary<JsonElement, SourceSpan>(JsonElementIdentityComparer.Instance);
#pragma warning restore MA0066
        var index = 0;
        Visit(root, tokenSpans, spansByElement, ref index);
        return new JsonSourceMap(root, tokenSpans, spansByElement);
    }

    public SourceSpan SpanFor(JsonElement element)
    {
        if (_spansByElement.TryGetValue(element, out var span))
        {
            return span;
        }

        var index = 0;
        return TryFindSpan(_root, _tokenSpans, element, ref index, out span) ? span : JsonImport.JsonSpan;
    }

    private static void Visit(
        JsonElement element,
        IReadOnlyList<SourceSpan> tokenSpans,
        Dictionary<JsonElement, SourceSpan> spansByElement,
        ref int index)
    {
        var span = index < tokenSpans.Count ? tokenSpans[index] : JsonImport.JsonSpan;
        index++;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                spansByElement[element] = span;
                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value, tokenSpans, spansByElement, ref index);
                }

                break;
            case JsonValueKind.Array:
                spansByElement[element] = span;
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, tokenSpans, spansByElement, ref index);
                }

                break;
        }
    }

    private static bool TryFindSpan(
        JsonElement current,
        IReadOnlyList<SourceSpan> tokenSpans,
        JsonElement target,
        ref int index,
        out SourceSpan span)
    {
        var currentSpan = index < tokenSpans.Count ? tokenSpans[index] : JsonImport.JsonSpan;
        index++;
#pragma warning disable MA0065
        if (current.Equals(target))
#pragma warning restore MA0065
        {
            span = currentSpan;
            return true;
        }

        switch (current.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in current.EnumerateObject())
                {
                    if (TryFindSpan(property.Value, tokenSpans, target, ref index, out span))
                    {
                        return true;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in current.EnumerateArray())
                {
                    if (TryFindSpan(item, tokenSpans, target, ref index, out span))
                    {
                        return true;
                    }
                }

                break;
        }

        span = JsonImport.JsonSpan;
        return false;
    }

    private sealed class JsonElementIdentityComparer : IEqualityComparer<JsonElement>
    {
        public static readonly JsonElementIdentityComparer Instance = new();

        private JsonElementIdentityComparer()
        {
        }

        public bool Equals(JsonElement x, JsonElement y)
        {
#pragma warning disable MA0065
            return x.Equals(y);
#pragma warning restore MA0065
        }

        public int GetHashCode(JsonElement obj)
        {
            var hash = new HashCode();
            hash.Add(obj.ValueKind);
            switch (obj.ValueKind)
            {
                case JsonValueKind.Object:
                    AddObjectHash(ref hash, obj);
                    break;
                case JsonValueKind.Array:
                    hash.Add(obj.GetArrayLength());
                    break;
            }

            return hash.ToHashCode();
        }

        private static void AddObjectHash(ref HashCode hash, JsonElement obj)
        {
            var count = 0;
            foreach (var property in obj.EnumerateObject())
            {
                count++;
                hash.Add(property.Name, StringComparer.Ordinal);
                AddShallowValueHash(ref hash, property.Value);
            }

            hash.Add(count);
        }

        private static void AddShallowValueHash(ref HashCode hash, JsonElement value)
        {
            hash.Add(value.ValueKind);
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    hash.Add(value.GetString(), StringComparer.Ordinal);
                    break;
                case JsonValueKind.Number when value.TryGetInt64(out var integer):
                    hash.Add(integer);
                    break;
                case JsonValueKind.Number when value.TryGetDouble(out var number):
                    hash.Add(number);
                    break;
                case JsonValueKind.Array:
                    hash.Add(value.GetArrayLength());
                    break;
            }
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
