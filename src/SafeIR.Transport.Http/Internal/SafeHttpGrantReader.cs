namespace SafeIR.Transport.Http.Internal;

using System.Globalization;
using System.Runtime.CompilerServices;
using SafeIR;

internal static class SafeHttpGrantReader
{
    private static readonly ConditionalWeakTable<CapabilityGrant, SafeHttpGrantOptions> Cache = new();

    public static SafeHttpGrantOptions Read(CapabilityGrant grant)
        => Cache.GetValue(grant, CreateOptions);

    private static SafeHttpGrantOptions CreateOptions(CapabilityGrant grant)
        => new(
            ReadSet(grant, "allowedSchemes", ["https"]),
            ReadSet(grant, "allowedHosts", []),
            ReadOptionalLong(grant, "maxRequestBytes"),
            ReadOptionalLong(grant, "maxResponseBytes"),
            ReadTimeout(grant),
            ReadBool(grant, "allowIpLiterals"),
            ReadBool(grant, "allowPrivateNetwork"));

    private static HashSet<string> ReadSet(CapabilityGrant grant, string key, string[] fallback)
    {
        var text = grant.Parameters.TryGetValue(key, out var value) ? value : string.Join(',', fallback);
        return text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ReadBool(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw Error($"parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static long? ReadOptionalLong(CapabilityGrant grant, string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            return null;
        }

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
        {
            throw Error($"parameter '{key}' is invalid");
        }

        return parsed;
    }

    private static TimeSpan ReadTimeout(CapabilityGrant grant)
    {
        var milliseconds = ReadOptionalLong(grant, "timeoutMs") ?? 2_000;
        if (milliseconds <= 0 || milliseconds > 60_000)
        {
            throw Error("timeout is outside the allowed range");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static SandboxRuntimeException Error(string message)
        => new(new SandboxError(SandboxErrorCode.PermissionDenied, $"net.http.get denied: {message}"));
}

internal sealed record SafeHttpGrantOptions(
    IReadOnlySet<string> AllowedSchemes,
    IReadOnlySet<string> AllowedHosts,
    long? MaxRequestBytes,
    long? MaxResponseBytes,
    TimeSpan Timeout,
    bool AllowIpLiterals,
    bool AllowPrivateNetwork);
