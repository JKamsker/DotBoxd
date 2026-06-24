using System.Globalization;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class KernelMethodDescriptorPayloadParser
{
    public static bool TryParse(string payload, out KernelMethodDescriptorPayload? descriptor)
    {
        descriptor = null;
        if (!TryObject(payload, out var properties) ||
            !TryBool(properties, "allocates", out var allocates) ||
            !TryStringArray(properties, "capabilities", out var capabilities) ||
            !TryString(properties, "contextType", out var contextType) ||
            !TryStringArray(properties, "effects", out var effects) ||
            !TryString(properties, "methodMetadataName", out var methodMetadataName) ||
            !TryString(properties, "normalizedSignature", out var normalizedSignature) ||
            !TryParameters(properties, out var parameters) ||
            !TryString(properties, "returnType", out var returnType) ||
            !TryString(properties, "source", out var source) ||
            !TryInt(properties, "version", out var version))
        {
            return false;
        }

        descriptor = new KernelMethodDescriptorPayload(
            version,
            contextType,
            methodMetadataName,
            normalizedSignature,
            returnType,
            allocates,
            new EquatableArray<string>(capabilities),
            new EquatableArray<string>(effects),
            new EquatableArray<KernelMethodDescriptorParameter>(parameters),
            source);
        return true;
    }

    private static bool TryObject(string json, out Dictionary<string, string> properties)
    {
        properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = SkipWhitespace(json, 0);
        if (index >= json.Length || json[index++] != '{')
        {
            return false;
        }

        while (true)
        {
            index = SkipWhitespace(json, index);
            if (index < json.Length && json[index] == '}')
            {
                index++;
                return SkipWhitespace(json, index) == json.Length;
            }

            if (!TryReadString(json, ref index, out var name))
            {
                return false;
            }

            index = SkipWhitespace(json, index);
            if (index >= json.Length || json[index++] != ':')
            {
                return false;
            }

            index = SkipWhitespace(json, index);
            var valueStart = index;
            if (!SkipValue(json, ref index))
            {
                return false;
            }

            properties[name] = json.Substring(valueStart, index - valueStart);
            index = SkipWhitespace(json, index);
            if (index < json.Length && json[index] == ',')
            {
                index++;
                continue;
            }

            if (index < json.Length && json[index] == '}')
            {
                continue;
            }

            return false;
        }
    }

    private static bool TryParameters(
        Dictionary<string, string> properties,
        out KernelMethodDescriptorParameter[] parameters)
    {
        parameters = [];
        if (!properties.TryGetValue("parameters", out var raw) ||
            !TryArray(raw, out var items))
        {
            return false;
        }

        var result = new KernelMethodDescriptorParameter[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (!TryObject(items[i], out var item) ||
                !TryString(item, "placeholder", out var placeholder) ||
                !TryString(item, "type", out var type))
            {
                return false;
            }

            result[i] = new KernelMethodDescriptorParameter(placeholder, type);
        }

        parameters = result;
        return true;
    }

    private static bool TryString(Dictionary<string, string> properties, string name, out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(name, out var raw))
        {
            return false;
        }

        var index = 0;
        return TryReadString(raw, ref index, out value) && SkipWhitespace(raw, index) == raw.Length;
    }

    private static bool TryStringArray(Dictionary<string, string> properties, string name, out string[] values)
    {
        values = [];
        if (!properties.TryGetValue(name, out var raw) ||
            !TryArray(raw, out var items))
        {
            return false;
        }

        values = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var index = 0;
            if (!TryReadString(items[i], ref index, out values[i]) ||
                SkipWhitespace(items[i], index) != items[i].Length)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBool(Dictionary<string, string> properties, string name, out bool value)
    {
        value = false;
        if (!properties.TryGetValue(name, out var raw))
        {
            return false;
        }

        if (string.Equals(raw, "true", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        return string.Equals(raw, "false", StringComparison.Ordinal);
    }

    private static bool TryInt(Dictionary<string, string> properties, string name, out int value)
    {
        value = 0;
        return properties.TryGetValue(name, out var raw) &&
               int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryArray(string json, out List<string> items)
    {
        items = [];
        var index = SkipWhitespace(json, 0);
        if (index >= json.Length || json[index++] != '[')
        {
            return false;
        }

        while (true)
        {
            index = SkipWhitespace(json, index);
            if (index < json.Length && json[index] == ']')
            {
                index++;
                return SkipWhitespace(json, index) == json.Length;
            }

            var valueStart = index;
            if (!SkipValue(json, ref index))
            {
                return false;
            }

            items.Add(json.Substring(valueStart, index - valueStart));
            index = SkipWhitespace(json, index);
            if (index < json.Length && json[index] == ',')
            {
                index++;
                continue;
            }

            if (index < json.Length && json[index] == ']')
            {
                continue;
            }

            return false;
        }
    }

}
