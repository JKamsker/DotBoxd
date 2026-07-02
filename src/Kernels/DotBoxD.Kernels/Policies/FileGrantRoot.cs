namespace DotBoxD.Kernels.Policies;

internal static class FileGrantRoot
{
    public static string NormalizeCanonicalDirectory(string root, string paramName)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("file grant root must be an absolute canonical path", paramName);
        }

        var fullPath = Path.GetFullPath(root);
        if (!PathsEqual(NormalizeRootForCompare(root), NormalizeRootForCompare(fullPath)))
        {
            throw new ArgumentException("file grant root must be an absolute canonical path", paramName);
        }

        return fullPath;
    }

    private static string NormalizeRootForCompare(string path)
        => Path.TrimEndingDirectorySeparator(path);

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
