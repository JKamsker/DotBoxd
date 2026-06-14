namespace SafeIR.Validation.Internal;

using SafeIR;

internal static class AllowedExtensionParameterValidator
{
    private const string Key = "allowedExtensions";

    public static void Validate(CapabilityGrant grant, List<SandboxDiagnostic> diagnostics)
    {
        if (!grant.Parameters.TryGetValue(Key, out var value))
        {
            return;
        }

        if (!HasAllowedExtensionValue(value))
        {
            Add(diagnostics, grant, $"parameter '{Key}' must contain at least one extension");
            return;
        }

        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] != ',')
            {
                continue;
            }

            var extension = TrimWhitespace(value.AsSpan(start, i - start));
            if (extension.Length == 0)
            {
                Add(diagnostics, grant, $"parameter '{Key}' must not contain empty values");
            }
            else if (!IsValidExtension(extension))
            {
                Add(diagnostics, grant, $"parameter '{Key}' contains invalid extension '{extension.ToString()}'");
            }

            start = i + 1;
        }
    }

    private static void Add(List<SandboxDiagnostic> diagnostics, CapabilityGrant grant, string message)
        => diagnostics.Add(new SandboxDiagnostic(
            "E-POLICY-GRANT-PARAM",
            $"grant '{grant.Id}' {message}"));

    private static bool HasAllowedExtensionValue(string value)
    {
        var start = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] != ',')
            {
                continue;
            }

            if (TrimWhitespace(value.AsSpan(start, i - start)).Length > 0)
            {
                return true;
            }

            start = i + 1;
        }

        return false;
    }

    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        var end = value.Length - 1;
        while (end >= start && char.IsWhiteSpace(value[end]))
        {
            end--;
        }

        return value[start..(end + 1)];
    }

    private static bool IsValidExtension(ReadOnlySpan<char> extension)
    {
        if (extension.Length <= 1 || extension[0] != '.')
        {
            return false;
        }

        for (var i = 1; i < extension.Length; i++)
        {
            var c = extension[i];
            if (char.IsControl(c) || char.IsWhiteSpace(c) || c is '/' or '\\' or '.')
            {
                return false;
            }
        }

        return true;
    }
}
