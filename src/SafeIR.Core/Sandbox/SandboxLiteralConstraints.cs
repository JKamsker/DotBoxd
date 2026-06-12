namespace SafeIR;

public static class SandboxLiteralConstraints
{
    public const int MaxTextLiteralLength = 65_536;

    private static readonly string[] ReservedWindowsDeviceNames = [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

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

        var start = 0;
        for (var i = 0; i <= path.Length; i++)
        {
            if (i < path.Length && path[i] != '/')
            {
                continue;
            }

            if (!IsValidPathSegment(path.AsSpan(start, i - start)))
            {
                return false;
            }

            start = i + 1;
        }

        return true;
    }

    private static bool IsValidPathSegment(ReadOnlySpan<char> segment)
    {
        if (segment.Length == 0 ||
            IsDotSegment(segment) ||
            ContainsControl(segment) ||
            segment[^1] is ' ' or '.')
        {
            return false;
        }

        var extensionStart = segment.IndexOf('.');
        var deviceName = extensionStart < 0 ? segment : segment[..extensionStart];
        return !IsReservedWindowsDeviceName(deviceName);
    }

    public static bool IsSandboxUri(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Contains('\\') &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsOpaqueId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
        {
            return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsLetterOrDigit(c) && c is not '_' and not '-' and not '.' and not ':')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDotSegment(ReadOnlySpan<char> segment)
        => (segment.Length == 1 && segment[0] == '.') ||
           (segment.Length == 2 && segment[0] == '.' && segment[1] == '.');

    private static bool ContainsControl(ReadOnlySpan<char> segment)
    {
        for (var i = 0; i < segment.Length; i++)
        {
            if (char.IsControl(segment[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReservedWindowsDeviceName(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < ReservedWindowsDeviceNames.Length; i++)
        {
            if (value.Equals(ReservedWindowsDeviceNames[i].AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static ValueShape TextShape(string value)
        => new(0, 0, 0, 0, value.Length, StringByteCount(value.Length));

    internal static long StringByteCount(int charLength)
    {
        try
        {
            return checked((long)charLength * sizeof(char));
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "string byte budget exhausted"));
        }
    }
}
