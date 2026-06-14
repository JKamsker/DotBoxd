namespace SafeIR.Runtime;

internal static class SafeHttpUriAudit
{
    public static string Sanitize(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{NormalizedAuthority(uri)}{AuditTextSanitizer.RedactPathSegments(uri.AbsolutePath)}"
            : "invalid-uri";

    public static bool MatchesAllowedAuthority(string allowed, Uri uri)
    {
        var authority = NormalizedAuthority(uri);
        if (StringComparer.OrdinalIgnoreCase.Equals(allowed, authority))
        {
            return true;
        }

        return uri.IsDefaultPort && StringComparer.OrdinalIgnoreCase.Equals(allowed, uri.Host);
    }

    public static bool SameUri(Uri left, Uri right)
        => StringComparer.OrdinalIgnoreCase.Equals(left.Scheme, right.Scheme) &&
           StringComparer.OrdinalIgnoreCase.Equals(NormalizedAuthority(left), NormalizedAuthority(right)) &&
           StringComparer.Ordinal.Equals(left.PathAndQuery, right.PathAndQuery);

    private static string NormalizedAuthority(Uri uri)
        => uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
}
