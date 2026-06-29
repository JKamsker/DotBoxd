namespace DotBoxD.Hosting.Http;

internal static class SafeHttpUriAudit
{
    public static string Sanitize(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{NormalizedAuthority(uri)}{SafePath(uri)}"
            : "invalid-uri";

    public static bool MatchesAllowedAuthority(string allowed, Uri uri)
    {
        var authority = NormalizedAuthority(uri);
        if (StringComparer.OrdinalIgnoreCase.Equals(allowed, authority))
        {
            return true;
        }

        return uri.IsDefaultPort &&
            (StringComparer.OrdinalIgnoreCase.Equals(allowed, uri.Host) ||
             MatchesExplicitDefaultPortAuthority(allowed, uri));
    }

    public static bool MatchesAllowedAuthority(IReadOnlySet<string> allowedHosts, Uri uri)
    {
        if (allowedHosts.Count == 0)
        {
            return false;
        }

        foreach (var allowed in allowedHosts)
        {
            if (MatchesAllowedAuthority(allowed, uri))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SameUri(Uri left, Uri right)
        => ReferenceEquals(left, right) ||
           StringComparer.OrdinalIgnoreCase.Equals(left.Scheme, right.Scheme) &&
           SameAuthority(left, right) &&
           StringComparer.Ordinal.Equals(left.PathAndQuery, right.PathAndQuery);

    private static string NormalizedAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    private static bool MatchesExplicitDefaultPortAuthority(string allowed, Uri uri)
        => TryGetDefaultPort(uri.Scheme, out var defaultPort) &&
           TryReadAuthorityPort(allowed, out var host, out var port) &&
           port == defaultPort &&
           StringComparer.OrdinalIgnoreCase.Equals(host, uri.Host);

    private static bool TryGetDefaultPort(string scheme, out int port)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(scheme, Uri.UriSchemeHttps))
        {
            port = 443;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(scheme, Uri.UriSchemeHttp))
        {
            port = 80;
            return true;
        }

        port = 0;
        return false;
    }

    private static bool TryReadAuthorityPort(string authority, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        var colon = authority.StartsWith("[", StringComparison.Ordinal)
            ? authority.IndexOf("]:", StringComparison.Ordinal) + 1
            : authority.LastIndexOf(':');
        if (colon <= 0 || colon == authority.Length - 1)
        {
            return false;
        }

        host = authority[..colon];
        var portText = authority[(colon + 1)..];
        return int.TryParse(
            portText,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out port);
    }

    private static bool SameAuthority(Uri left, Uri right)
        => left.Port == right.Port &&
           StringComparer.OrdinalIgnoreCase.Equals(left.Host, right.Host);

    private static string SafePath(Uri uri)
        => AuditTextSanitizer.RedactPathSegments(Uri.UnescapeDataString(uri.AbsolutePath));
}
