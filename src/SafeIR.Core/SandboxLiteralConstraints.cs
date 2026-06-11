namespace SafeIR;

public static class SandboxLiteralConstraints
{
    public const int MaxTextLiteralLength = 65_536;

    public static bool IsPortableRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\\') ||
            path.Contains(':') ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(path, UriKind.Absolute, out _) ||
            Path.IsPathRooted(path) ||
            path.Split('/').Any(segment => segment is "")) {
            return false;
        }

        return true;
    }

    public static bool IsSandboxUri(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           !value.Contains('\\') &&
           Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           string.IsNullOrEmpty(uri.UserInfo);

    internal static ValueShape TextShape(string value)
        => new(0, 0, 0, 0, value.Length, value.Length * sizeof(char));
}
