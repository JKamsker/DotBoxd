using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Internal;

internal static class PersistentCompiledArtifactCachePathGuard
{
    public static void ValidateEntryPath(string rootDirectory, string entryPath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var entry = Path.GetFullPath(entryPath);
        EnsureUnderRoot(root, entry);

        var relative = Path.GetRelativePath(root, entry);
        var current = root;
        foreach (var component in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (Directory.Exists(current))
            {
                EnsureNotReparsePoint(current);
                continue;
            }

            if (File.Exists(current))
            {
                throw Invalid("compiled cache path component is not a directory");
            }
        }
    }

    private static void EnsureUnderRoot(string rootDirectory, string entryPath)
    {
        var root = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!entryPath.StartsWith(root, comparison))
        {
            throw Invalid("compiled cache entry path escaped the cache root");
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        var info = new DirectoryInfo(path);
        info.Refresh();
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Invalid("compiled cache path must not contain reparse points");
        }
    }

    private static SandboxRuntimeException Invalid(string message)
        => new(new SandboxError(SandboxErrorCode.CacheInvalid, message));
}
