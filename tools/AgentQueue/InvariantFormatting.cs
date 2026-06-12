using System.Globalization;

namespace AgentQueue;

internal static class InvariantFormatting
{
    public static string ToStringInvariant(this int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
