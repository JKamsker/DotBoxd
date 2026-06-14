namespace SafeIR.Transport.Http.Internal;

using System.Globalization;
using SafeIR;

internal static class SafeHttpGrantValidator
{
    public static void Validate(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        RequireAllowedKeys(
            grant,
            diagnostics,
            [
                "allowedHosts",
                "allowedSchemes",
                "maxRequestBytes",
                "maxResponseBytes",
                "timeoutMs",
                "allowIpLiterals",
                "allowPrivateNetwork"
            ]);
        RequireCsv(
            grant,
            diagnostics,
            "allowedHosts",
            SafeHttpGrantValueValidator.IsAllowedAuthority,
            "a host or host:port authority");
        RequireCsv(
            grant,
            diagnostics,
            "allowedSchemes",
            SafeHttpGrantValueValidator.IsAllowedScheme,
            "http or https");
        RequireOptionalNonNegativeLong(grant, diagnostics, "maxRequestBytes");
        RequireNonNegativeLong(grant, diagnostics, "maxResponseBytes");
        RequireRangeLong(grant, diagnostics, "timeoutMs", min: 1, max: 60_000);
        RequireOptionalBool(grant, diagnostics, "allowIpLiterals");
        RequireOptionalBool(grant, diagnostics, "allowPrivateNetwork");
    }

    private static void RequireAllowedKeys(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        IEnumerable<string> allowedKeys)
    {
        var allowed = allowedKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in grant.Parameters.Keys)
        {
            if (!allowed.Contains(key))
            {
                Add(diagnostics, grant, $"parameter '{key}' is not supported");
            }
        }
    }

    private static void RequireCsv(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        string key,
        Func<string, bool> isValid,
        string description)
    {
        if (!grant.Parameters.TryGetValue(key, out var value))
        {
            Add(diagnostics, grant, $"parameter '{key}' must contain at least one value");
            return;
        }

        var values = value.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length == 0 || values.Any(item => string.IsNullOrWhiteSpace(item)))
        {
            Add(diagnostics, grant, $"parameter '{key}' must not contain empty values");
            return;
        }

        if (values.Any(item => !isValid(item)))
        {
            Add(diagnostics, grant, $"parameter '{key}' must contain only {description} values");
        }
    }

    private static void RequireNonNegativeLong(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        string key)
        => RequireRangeLong(grant, diagnostics, key, min: 0, max: long.MaxValue);

    private static void RequireOptionalNonNegativeLong(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (grant.Parameters.ContainsKey(key))
        {
            RequireNonNegativeLong(grant, diagnostics, key);
        }
    }

    private static void RequireRangeLong(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        string key,
        long min,
        long max)
    {
        if (!grant.Parameters.TryGetValue(key, out var value) ||
            !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < min ||
            parsed > max)
        {
            Add(diagnostics, grant, $"parameter '{key}' must be between {min} and {max}");
        }
    }

    private static void RequireOptionalBool(
        CapabilityGrant grant,
        ICollection<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (grant.Parameters.TryGetValue(key, out var value) && !bool.TryParse(value, out _))
        {
            Add(diagnostics, grant, $"parameter '{key}' must be a boolean");
        }
    }

    private static void Add(ICollection<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));
}
