namespace SafeIR;

public static class SandboxLiteralConstraints
{
    public const int MaxTextLiteralLength = 65_536;

    private static readonly HashSet<string> ReservedWindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase) {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool IsPortableRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\\') ||
            path.Contains(':') ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out _) ||
            Path.IsPathRooted(path))
        {
            return false;
        }

        return path.Split('/').All(IsValidPathSegment);
    }

    private static bool IsValidPathSegment(string segment)
    {
        if (segment is "" or "." or ".." ||
            segment.Any(char.IsControl) ||
            segment.EndsWith(' ') ||
            segment.EndsWith('.'))
        {
            return false;
        }

        var extensionStart = segment.IndexOf('.', StringComparison.Ordinal);
        var deviceName = extensionStart < 0 ? segment : segment[..extensionStart];
        return !ReservedWindowsDeviceNames.Contains(deviceName);
    }

    public static bool IsSandboxUri(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Contains('\\') &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsOpaqueId(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Length <= 256 &&
           value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':');

    internal static ValueShape TextShape(string value)
        => new(0, 0, 0, 0, value.Length, value.Length * sizeof(char));
}
