using DotBoxD.Kernels.Model;

namespace DotBoxD.Queryable.Serialization;

internal static class EventQueryJsonStringSafety
{
    public static string RequireWellFormedUtf16(string value, string name)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsHighSurrogate(current))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    i++;
                    continue;
                }

                throw MalformedUtf16(name);
            }

            if (char.IsLowSurrogate(current))
            {
                throw MalformedUtf16(name);
            }
        }

        return value;
    }

    private static SandboxValidationException MalformedUtf16(string name)
        => new([
            new SandboxDiagnostic(
                "DBXQ001",
                $"EventQueryJson structural string '{name}' contains malformed UTF-16 text with an unpaired surrogate")
        ]);
}
