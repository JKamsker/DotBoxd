namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class KernelMethodDescriptorPayloadParser
{
    private static bool SkipValue(string json, ref int index)
        => index < json.Length && json[index] switch
        {
            '"' => TryReadString(json, ref index, out _),
            '[' => SkipBalanced(json, ref index, '[', ']'),
            '{' => SkipBalanced(json, ref index, '{', '}'),
            _ => SkipPrimitive(json, ref index)
        };

    private static bool SkipBalanced(string json, ref int index, char open, char close)
    {
        if (json[index++] != open)
        {
            return false;
        }

        var depth = 1;
        while (index < json.Length)
        {
            var c = json[index++];
            if (c == '"')
            {
                index--;
                if (!TryReadString(json, ref index, out _))
                {
                    return false;
                }

                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close && --depth == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SkipPrimitive(string json, ref int index)
    {
        var start = index;
        while (index < json.Length && json[index] is not (',' or ']' or '}'))
        {
            index++;
        }

        index = SkipWhitespaceBack(json, index);
        return index > start;
    }

    private static bool TryReadString(string json, ref int index, out string value)
    {
        value = string.Empty;
        index = SkipWhitespace(json, index);
        if (index >= json.Length || json[index++] != '"')
        {
            return false;
        }

        var builder = new System.Text.StringBuilder();
        while (index < json.Length)
        {
            var c = json[index++];
            if (c == '"')
            {
                value = builder.ToString();
                return true;
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            if (index >= json.Length)
            {
                return false;
            }

            var escaped = json[index++] switch
            {
                '"' => '"',
                '\\' => '\\',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => '\0'
            };
            if (escaped == '\0')
            {
                return false;
            }

            builder.Append(escaped);
        }

        return false;
    }

    private static int SkipWhitespace(string value, int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        return index;
    }

    private static int SkipWhitespaceBack(string value, int index)
    {
        while (index > 0 && char.IsWhiteSpace(value[index - 1]))
        {
            index--;
        }

        return index;
    }
}
