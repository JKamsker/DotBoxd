namespace DotBoxD.Kernels.Runtime;

using System.Text.RegularExpressions;

public static partial class AuditTextSanitizer
{
    private const string Redacted = "[redacted]";

    // ExplicitCapture: every extraction below is by named group, so unnamed groups need not be
    // captured. All patterns run over untrusted audit text, so each Regex is given an explicit
    // match timeout to bound worst-case backtracking (MA0009 regex-DoS hardening).
    private const RegexOptions RedactionOptions =
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;
    private static readonly TimeSpan RedactionTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex AuthorizationHeaderRegex = new(
        "(?i)(?<key>\\bauthorization\\s*[:=]\\s*)(?:(?<scheme>bearer|basic)\\s+)?(?<value>[^\\s,;]+)",
        RedactionOptions,
        RedactionTimeout);

    private static readonly Regex SecretRegex = new(
        "(?i)(?<key>\\b(?:password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|session[_-]?token|api[_-]?key|account[_-]?key|client[_-]?secret|private[_-]?key)\\s*[:=]\\s*)(?<value>[^\\s,;]+)",
        RedactionOptions,
        RedactionTimeout);

    private static readonly Regex AuthSchemeRegex = new(
        "(?i)\\b(?<scheme>bearer|basic)\\s+[A-Za-z0-9._~+/=-]+",
        RedactionOptions,
        RedactionTimeout);

    private static readonly Regex UriCredentialRegex = new(
        "(?<prefix>\\b[A-Za-z][A-Za-z0-9+.-]*://)[^\\s/@:]+:[^\\s/@]+@",
        RedactionOptions,
        RedactionTimeout);

    // Single source of truth for path markers: the regex, cheap prefilter, and standalone-marker
    // classifier all consume this table so the prefilter stays a superset of the redaction matcher.
    private static readonly string[] SecretPathMarkers =
    [
        "authorization", "bearer", "credential", "key",
        "password", "passwd", "pwd", "secret",
        "session", "signature",
        "token"
    ];

    private static readonly char[] SecretPathMarkerInitials = CreateSecretPathMarkerInitials();
    private static readonly int MaxSecretPathMarkerLength = GetMaxSecretPathMarkerLength();
    private static readonly Regex SecretPathSegmentRegex = CreateSecretPathSegmentRegex();

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
        sanitized = UriCredentialRegex.Replace(sanitized, "${prefix}[redacted]@");
        sanitized = AuthorizationHeaderRegex.Replace(
            sanitized,
            match => match.Groups["key"].Value +
                     (match.Groups["scheme"].Success ? match.Groups["scheme"].Value + " " : "") +
                     "[redacted]");
        sanitized = SecretRegex.Replace(sanitized, match => match.Groups["key"].Value + "[redacted]");
        return AuthSchemeRegex.Replace(
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
        if (!MayContainSecretPathSegment(path))
        {
            return path;
        }

        var segments = path.Split('/');
        var previousWasSecretMarker = false;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var redaction = ClassifySecretPathSegment(segment);
            if (previousWasSecretMarker || redaction.Redact)
            {
                segments[i] = Redacted;
            }

            previousWasSecretMarker = redaction.RedactFollowingSegment;
        }

        return string.Join("/", segments);
    }

    private static bool MayContainSecretPathSegment(string path)
    {
        for (var i = 0; i < path.Length; i++)
        {
            var remaining = path.AsSpan(i);
            if (path[i] == '%' || StartsSecretPathMarker(remaining))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsSecretPathMarker(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        if (value[0] <= '\u007f' && !HasSecretPathMarkerInitial(value[0]))
        {
            return false;
        }

        var comparison = value[0] > '\u007f' || ContainsNonAscii(value, MaxSecretPathMarkerLength)
            ? StringComparison.InvariantCultureIgnoreCase
            : StringComparison.OrdinalIgnoreCase;
        return StartsAnySecretPathMarker(value, comparison);
    }

    private static Regex CreateSecretPathSegmentRegex()
    {
        var markers = string.Join("|", SecretPathMarkers.Select(Regex.Escape));
        return new Regex(
            $"(?i)(^|[-_.])({markers})([-_.=:]|$)",
            RedactionOptions,
            RedactionTimeout);
    }

    private static char[] CreateSecretPathMarkerInitials()
    {
        var initials = new List<char>(SecretPathMarkers.Length);
        foreach (var marker in SecretPathMarkers)
        {
            var initial = char.ToUpperInvariant(marker[0]);
            if (!initials.Contains(initial))
            {
                initials.Add(initial);
            }
        }

        return initials.ToArray();
    }

    private static int GetMaxSecretPathMarkerLength()
    {
        var max = 0;
        foreach (var marker in SecretPathMarkers)
        {
            max = Math.Max(max, marker.Length);
        }

        return max;
    }

    private static bool StartsAnySecretPathMarker(ReadOnlySpan<char> value, StringComparison comparison)
    {
        foreach (var marker in SecretPathMarkers)
        {
            if (value.StartsWith(marker, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSecretPathMarkerInitial(char value)
    {
        var upper = char.ToUpperInvariant(value);
        foreach (var initial in SecretPathMarkerInitials)
        {
            if (upper == initial)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsNonAscii(ReadOnlySpan<char> value, int length)
    {
        var count = Math.Min(value.Length, length);
        for (var i = 0; i < count; i++)
        {
            if (value[i] > '\u007f')
            {
                return true;
            }
        }

        return false;
    }

    private static (bool Redact, bool RedactFollowingSegment) ClassifySecretPathSegment(string segment)
    {
        var normalized = DecodePathSegment(segment).Trim();
        if (normalized.Length == 0)
        {
            return (false, false);
        }

        var decodedSegments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (decodedSegments.Length > 1)
        {
            for (var i = 0; i < decodedSegments.Length; i++)
            {
                if (SecretPathSegmentRegex.IsMatch(decodedSegments[i]))
                {
                    return (true, false);
                }
            }

            return (false, false);
        }

        if (!SecretPathSegmentRegex.IsMatch(normalized))
        {
            return (false, false);
        }

        return (true, IsStandaloneSecretMarker(normalized));
    }

    private static string DecodePathSegment(string segment)
    {
        try
        {
            return global::System.Uri.UnescapeDataString(segment);
        }
        catch (global::System.UriFormatException)
        {
            return segment;
        }
    }

    private static bool IsStandaloneSecretMarker(string segment)
    {
        var normalized = segment.Trim().Trim('-', '_', '.');
        return IsExactSecretPathMarker(normalized) ||
               normalized.EndsWith("-key", StringComparison.InvariantCultureIgnoreCase) ||
               normalized.EndsWith("_key", StringComparison.InvariantCultureIgnoreCase) ||
               normalized.EndsWith(".key", StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool IsExactSecretPathMarker(string value)
    {
        foreach (var marker in SecretPathMarkers)
        {
            if (value.Equals(marker, StringComparison.InvariantCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
