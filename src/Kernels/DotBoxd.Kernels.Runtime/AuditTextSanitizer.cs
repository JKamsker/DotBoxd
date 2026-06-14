namespace DotBoxd.Kernels.Runtime;

using System.Text.RegularExpressions;

public static partial class AuditTextSanitizer
{
    private const string Redacted = "[redacted]";

    public static string SanitizeAndRedact(string message)
    {
        if (!RequiresSanitization(message))
        {
            return message;
        }

        var chars = message.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        var sanitized = new string(chars);
        sanitized = UriCredentialPattern().Replace(sanitized, "${prefix}[redacted]@");
        sanitized = AuthorizationHeaderPattern().Replace(
            sanitized,
            match => match.Groups["key"].Value +
                     (match.Groups["scheme"].Success ? match.Groups["scheme"].Value + " " : "") +
                     "[redacted]");
        sanitized = SecretPattern().Replace(sanitized, match => match.Groups["key"].Value + "[redacted]");
        return AuthSchemePattern().Replace(
            sanitized,
            match => match.Groups["scheme"].Value + " " + Redacted);
    }

    /// <summary>
    /// Cheap, allocation-free prefilter that returns <c>true</c> only when
    /// <see cref="SanitizeAndRedact"/> could observably change <paramref name="message"/>.
    /// It is intentionally conservative: it never returns <c>false</c> for text that any
    /// redaction pass would rewrite. Control characters are sanitized; every redaction
    /// regex requires a credential separator ('@', ':', or '=') or an auth scheme keyword
    /// ("bearer"/"basic"), so the absence of all of these guarantees the message is clean.
    /// </summary>
    private static bool RequiresSanitization(string message)
    {
        foreach (var c in message)
        {
            if (char.IsControl(c) || c is '@' or ':' or '=')
            {
                return true;
            }
        }

        return message.Contains("bearer", StringComparison.OrdinalIgnoreCase)
            || message.Contains("basic", StringComparison.OrdinalIgnoreCase);
    }

    public static string RedactPathSegments(string path)
    {
        var segments = path.Split('/');
        var previousWasSecretMarker = false;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var isSecret = IsSecretSegment(segment);
            if (previousWasSecretMarker || isSecret)
            {
                segments[i] = Redacted;
            }

            previousWasSecretMarker = isSecret;
        }

        return string.Join("/", segments);
    }

    private static bool IsSecretSegment(string segment)
    {
        var normalized = segment.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        return SecretPathSegmentPattern().IsMatch(normalized);
    }

    [GeneratedRegex("(?i)(?<key>\\bauthorization\\s*[:=]\\s*)(?:(?<scheme>bearer|basic)\\s+)?(?<value>[^\\s,;]+)")]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex("(?i)(?<key>\\b(?:password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|session[_-]?token|api[_-]?key|account[_-]?key|client[_-]?secret|private[_-]?key)\\s*[:=]\\s*)(?<value>[^\\s,;]+)")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(?i)\\b(?<scheme>bearer|basic)\\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex AuthSchemePattern();

    [GeneratedRegex("(?<prefix>\\b[A-Za-z][A-Za-z0-9+.-]*://)[^\\s/@:]+:[^\\s/@]+@")]
    private static partial Regex UriCredentialPattern();

    [GeneratedRegex("(?i)(^|[-_.])(authorization|bearer|credential|key|password|passwd|pwd|secret|session|signature|token)([-_.]|$)")]
    private static partial Regex SecretPathSegmentPattern();
}
