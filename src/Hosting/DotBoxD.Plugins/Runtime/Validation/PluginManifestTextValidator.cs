using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Validation;

internal static class PluginManifestTextValidator
{
    public static void ValidatePluginId(string value, List<SandboxDiagnostic> diagnostics)
    {
        if (!ValidateText(value, "plugin id", diagnostics))
        {
            return;
        }

        if (!IsStablePluginId(value))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK050",
                "Plugin manifest plugin id must be a stable identifier: use ASCII letters, digits, '.', '_' or '-', " +
                "start and end with a letter or digit, do not use empty dot segments, or use a generated anonymous InvokeAsync id."));
        }
    }

    public static bool ValidateText(string value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK050",
                $"Plugin manifest {description} must be non-empty and must not contain control characters."));
            return false;
        }

        if (SandboxDescriptorGuards.ContainsForbiddenDescriptor(value))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK050",
                $"Plugin manifest {description} looks like a forbidden CLR or IL descriptor."));
            return false;
        }

        return true;
    }

    private static bool IsStablePluginId(string value)
    {
        if (IsGeneratedAnonymousPluginId(value))
        {
            return true;
        }

        if (value.Length > 128 ||
            !IsAsciiLetterOrDigit(value[0]) ||
            !IsAsciiLetterOrDigit(value[value.Length - 1]))
        {
            return false;
        }

        var previousWasDot = false;
        foreach (var ch in value)
        {
            if (ch == '.')
            {
                if (previousWasDot)
                {
                    return false;
                }

                previousWasDot = true;
                continue;
            }

            previousWasDot = false;
            if (!IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsGeneratedAnonymousPluginId(string value)
    {
        const string prefix = "$anon:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length == prefix.Length)
        {
            return false;
        }

        for (var i = prefix.Length; i < value.Length; i++)
        {
            if (!IsAsciiLetterOrDigit(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetterOrDigit(char ch)
        => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
}
