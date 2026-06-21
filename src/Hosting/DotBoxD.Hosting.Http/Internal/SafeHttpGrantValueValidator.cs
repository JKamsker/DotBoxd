namespace DotBoxD.Hosting.Http.Internal;

internal static class SafeHttpGrantValueValidator
{
    private static readonly char[] ForbiddenAuthorityCharacters = ['/', '\\', '?', '#', '@', ','];

    public static bool IsAllowedAuthority(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOfAny(ForbiddenAuthorityCharacters) >= 0)
        {
            return false;
        }

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            return Uri.TryCreate($"https://{value}/", UriKind.Absolute, out var uri) &&
                string.IsNullOrEmpty(uri.UserInfo);
        }

        var host = value;
        var colon = value.LastIndexOf(':');
        if (colon >= 0)
        {
            host = value[..colon];
            var portText = value[(colon + 1)..];
            if (host.Contains(':') ||
                !int.TryParse(portText, System.Globalization.CultureInfo.InvariantCulture, out var port) ||
                port is < 1 or > 65_535)
            {
                return false;
            }
        }

        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    public static bool IsAllowedScheme(string value)
        => string.Equals(value, "https", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "http", StringComparison.OrdinalIgnoreCase);
}
