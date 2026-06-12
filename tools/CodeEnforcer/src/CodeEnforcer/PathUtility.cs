namespace CodeEnforcer;

internal static class PathUtility
{
    public static string Normalize(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('.', '/');

    public static string GetDirectory(string path)
    {
        string normalized = Normalize(path);
        int lastSlash = normalized.LastIndexOf('/');
        return lastSlash < 0 ? "." : normalized[..lastSlash];
    }
}
