namespace DotBoxd.Kernels.Validation;

using System.Globalization;
using DotBoxd.Kernels;

internal static class PolicyGrantValidator
{
    private static readonly string[] NoAllowedParameterKeys = [];
    private static readonly string[] FileReadParameterKeys = ["root", "maxBytesPerRun", "allowedExtensions"];
    private static readonly string[] FileWriteParameterKeys =
        ["root", "maxBytesPerRun", "allowCreate", "allowOverwrite", "allowedExtensions"];

    public static void Validate(
        SandboxPolicy policy,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        var now = policy.GrantClock;
        AddDuplicateActiveGrantDiagnostics(policy.Grants, now, diagnostics);
        foreach (var grant in policy.Grants)
        {
            if (IsActive(grant, now))
            {
                ValidateGrant(grant, bindings, requiredCapabilities, diagnostics);
            }
        }
    }

    private static bool IsActive(CapabilityGrant grant, DateTimeOffset now)
        => grant.ExpiresAt is null || grant.ExpiresAt > now;

    private static void AddDuplicateActiveGrantDiagnostics(
        IReadOnlyList<CapabilityGrant> grants,
        DateTimeOffset now,
        List<SandboxDiagnostic> diagnostics)
    {
        if (grants.Count < 2)
        {
            return;
        }

        var counts = new Dictionary<string, int>(grants.Count, StringComparer.Ordinal);
        var nullCount = 0;
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (IsActive(grant, now))
            {
                IncrementCount(counts, grant.Id, ref nullCount);
            }
        }

        var reportedNull = false;
        for (var i = 0; i < grants.Count; i++)
        {
            var grant = grants[i];
            if (IsActive(grant, now) &&
                ShouldReportDuplicate(counts, grant.Id, nullCount, ref reportedNull))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "E-POLICY-GRANT",
                    $"capability '{grant.Id}' has multiple active grants"));
            }
        }
    }

    private static void IncrementCount(Dictionary<string, int> counts, string? value, ref int nullCount)
    {
        if (value is null)
        {
            nullCount++;
            return;
        }

        counts.TryGetValue(value, out var count);
        counts[value] = count + 1;
    }

    private static bool ShouldReportDuplicate(
        Dictionary<string, int> counts,
        string? value,
        int nullCount,
        ref bool reportedNull)
    {
        if (value is null)
        {
            if (nullCount < 2 || reportedNull)
            {
                return false;
            }

            reportedNull = true;
            return true;
        }

        if (!counts.TryGetValue(value, out var count) || count < 2)
        {
            return false;
        }

        counts[value] = 0;
        return true;
    }

    private static void ValidateGrant(
        CapabilityGrant grant,
        IBindingCatalog bindings,
        IReadOnlySet<string> requiredCapabilities,
        List<SandboxDiagnostic> diagnostics)
    {
        switch (grant.Id)
        {
            case "file.read":
                ValidateFileGrant(grant, diagnostics, allowWriteFlags: false);
                break;
            case "file.write":
                ValidateFileGrant(grant, diagnostics, allowWriteFlags: true);
                break;
            case "time.now" or "random" or "log.write":
                RequireAllowedKeys(grant, diagnostics, NoAllowedParameterKeys);
                break;
            default:
                if (bindings.TryGetCapabilityGrantValidator(grant.Id, out var validator))
                {
                    validator(grant, diagnostics);
                    return;
                }

                if (!SupportsAnyRequired(grant.Id, requiredCapabilities))
                {
                    diagnostics.Add(new SandboxDiagnostic(
                        "E-POLICY-GRANT",
                        $"grant '{grant.Id}' is not supported by the prepared module"));
                }

                break;
        }
    }

    // A grant supports the module if it is exactly a required capability, or a wildcard pattern
    // (e.g. "game.world.monster.*") that authorizes at least one concrete required capability.
    private static bool SupportsAnyRequired(string grantId, IReadOnlySet<string> requiredCapabilities)
    {
        if (requiredCapabilities.Contains(grantId))
        {
            return true;
        }

        if (!CapabilityPattern.IsWildcard(grantId))
        {
            return false;
        }

        foreach (var required in requiredCapabilities)
        {
            if (CapabilityPattern.Matches(grantId, required))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateFileGrant(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        bool allowWriteFlags)
    {
        var allowed = allowWriteFlags ? FileWriteParameterKeys : FileReadParameterKeys;
        RequireAllowedKeys(grant, diagnostics, allowed);
        RequireNonEmpty(grant, diagnostics, "root");
        RequireAbsoluteCanonicalRoot(grant, diagnostics);
        RequireNonNegativeLong(grant, diagnostics, "maxBytesPerRun");
        RequireAllowedExtensions(grant, diagnostics);
        if (allowWriteFlags)
        {
            RequireOptionalBool(grant, diagnostics, "allowCreate");
            RequireOptionalBool(grant, diagnostics, "allowOverwrite");
        }
    }

    private static void RequireAllowedKeys(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlyList<string> allowedKeys)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            if (!ContainsKey(allowedKeys, key))
            {
                Add(diagnostics, grant, $"parameter '{key}' is not supported");
            }
        }
    }

    private static bool ContainsKey(IReadOnlyList<string> allowedKeys, string key)
    {
        for (var i = 0; i < allowedKeys.Count; i++)
        {
            if (string.Equals(allowedKeys[i], key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void RequireNonEmpty(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            Add(diagnostics, grant, $"parameter '{key}' is required");
        }
    }

    private static void RequireCsv(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (!grant.Parameters.TryGetValue(key, out var value) ||
            value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length == 0)
        {
            Add(diagnostics, grant, $"parameter '{key}' must contain at least one value");
        }
    }

    private static void RequireAbsoluteCanonicalRoot(CapabilityGrant grant, List<SandboxDiagnostic> diagnostics)
    {
        if (!grant.Parameters.TryGetValue("root", out var root) || string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(root);
            if (!Path.IsPathFullyQualified(root) ||
                !PathsEqual(NormalizeRootForCompare(root), NormalizeRootForCompare(fullPath)))
            {
                Add(diagnostics, grant, "parameter 'root' must be an absolute canonical path");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Add(diagnostics, grant, "parameter 'root' must be an absolute canonical path");
        }
    }

    private static void RequireAllowedExtensions(CapabilityGrant grant, List<SandboxDiagnostic> diagnostics)
        => AllowedExtensionParameterValidator.Validate(grant, diagnostics);

    private static void RequireNonNegativeLong(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        string key)
        => RequireRangeLong(grant, diagnostics, key, min: 0, max: long.MaxValue);

    private static void RequireRangeLong(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
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
        List<SandboxDiagnostic> diagnostics,
        string key)
    {
        if (grant.Parameters.TryGetValue(key, out var value) && !bool.TryParse(value, out _))
        {
            Add(diagnostics, grant, $"parameter '{key}' must be a boolean");
        }
    }

    private static void Add(List<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));

    private static string NormalizeRootForCompare(string path)
        => Path.TrimEndingDirectorySeparator(path);

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
