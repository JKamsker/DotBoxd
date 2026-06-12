using System.Globalization;

namespace CodeEnforcer;

internal static class InvariantFormatting
{
    public static string ToStringInvariant(this int value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
