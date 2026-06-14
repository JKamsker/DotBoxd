using System.Globalization;

namespace AgentQueue.Infrastructure;

internal static class InvariantFormatting
{
    public static string ToStringInvariant(this int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
