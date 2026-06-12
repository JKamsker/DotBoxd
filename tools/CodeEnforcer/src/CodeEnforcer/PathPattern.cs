using System.Text.RegularExpressions;

namespace CodeEnforcer;

internal static class PathPattern
{
    public static bool IsMatch(string path, string pattern)
    {
        string normalizedPath = PathUtility.Normalize(path);
        string normalizedPattern = PathUtility.Normalize(pattern);
        if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(normalizedPath, normalizedPattern, StringComparison.Ordinal);
        }

        string regex = "^" + Regex.Escape(normalizedPattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(normalizedPath, regex, RegexOptions.CultureInvariant);
    }
}
