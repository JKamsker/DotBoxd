using System.Text.Json;

namespace SafeIR;

internal sealed class JsonSourceMap
{
    private readonly string _json;
    private readonly Dictionary<string, Queue<SourceSpan>> _spansByRawText;

    private JsonSourceMap(string json)
    {
        _json = json;
        _spansByRawText = new Dictionary<string, Queue<SourceSpan>>(StringComparer.Ordinal);
    }

    public static JsonSourceMap Create(string json, JsonElement root)
    {
        var map = new JsonSourceMap(json);
        var cursor = 0;
        map.Visit(root, ref cursor);
        return map;
    }

    public SourceSpan SpanFor(JsonElement element)
    {
        var rawText = element.GetRawText();
        if (_spansByRawText.TryGetValue(rawText, out var spans) && spans.Count > 0)
        {
            return spans.Dequeue();
        }

        var index = _json.IndexOf(rawText, StringComparison.Ordinal);
        return index < 0 ? JsonImport.JsonSpan : SpanAt(index);
    }

    private void Visit(JsonElement element, ref int cursor)
    {
        var rawText = element.GetRawText();
        var index = _json.IndexOf(rawText, cursor, StringComparison.Ordinal);
        if (index < 0)
        {
            index = _json.IndexOf(rawText, StringComparison.Ordinal);
        }

        if (index >= 0)
        {
            Add(rawText, SpanAt(index));
            cursor = Math.Max(cursor, index + 1);
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value, ref cursor);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, ref cursor);
                }

                break;
        }
    }

    private void Add(string rawText, SourceSpan span)
    {
        if (!_spansByRawText.TryGetValue(rawText, out var spans))
        {
            spans = new Queue<SourceSpan>();
            _spansByRawText.Add(rawText, spans);
        }

        spans.Enqueue(span);
    }

    private SourceSpan SpanAt(int index)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < index; i++)
        {
            if (_json[i] == '\r')
            {
                line++;
                column = 1;
                if (i + 1 < index && _json[i + 1] == '\n')
                {
                    i++;
                }
            }
            else if (_json[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new SourceSpan(line, column);
    }
}
