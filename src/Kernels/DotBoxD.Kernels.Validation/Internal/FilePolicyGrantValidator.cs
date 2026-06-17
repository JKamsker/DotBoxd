using System.Globalization;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Internal;

using DotBoxD.Kernels;

internal static class FilePolicyGrantValidator
{
    private static readonly string[] FileReadParameterKeys = ["root", "maxBytesPerRun", "allowedExtensions"];
    private static readonly string[] FileWriteParameterKeys =
        ["root", "maxBytesPerRun", "allowCreate", "allowOverwrite", "allowedExtensions"];

    public static void ValidateRead(CapabilityGrant grant, List<SandboxDiagnostic> diagnostics)
        => Validate(grant, diagnostics, allowWriteFlags: false);

    public static void ValidateWrite(CapabilityGrant grant, List<SandboxDiagnostic> diagnostics)
        => Validate(grant, diagnostics, allowWriteFlags: true);

    private static void Validate(
        CapabilityGrant grant,
        List<SandboxDiagnostic> diagnostics,
        bool allowWriteFlags)
    {
        var allowed = allowWriteFlags ? FileWriteParameterKeys : FileReadParameterKeys;
        RequireAllowedKeys(grant, diagnostics, allowed);
        RequireNonEmpty(grant, diagnostics, "root");
        RequireAbsoluteCanonicalRoot(grant, diagnostics);
        RequireNonNegativeLong(grant, diagnostics, "maxBytesPerRun");
        AllowedExtensionParameterValidator.Validate(grant, diagnostics);
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
