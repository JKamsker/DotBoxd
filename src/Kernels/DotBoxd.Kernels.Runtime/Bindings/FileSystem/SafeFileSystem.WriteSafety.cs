namespace DotBoxd.Kernels.Runtime;

using DotBoxd.Kernels;

public static partial class SafeFileSystem
{
    private static readonly AsyncLocal<Func<string>?> TempSuffixFactory = new();
    private static readonly AsyncLocal<Action?> BeforeTempCreateForTests = new();
    private static readonly AsyncLocal<Action<string>?> BeforeDirectoryCreateForTests = new();

    internal static IDisposable UseTempSuffixForTests(string suffix)
    {
        var previous = TempSuffixFactory.Value;
        TempSuffixFactory.Value = () => suffix;
        return new TempSuffixScope(previous);
    }

    internal static IDisposable UseBeforeTempCreateForTests(Action action)
    {
        var previous = BeforeTempCreateForTests.Value;
        BeforeTempCreateForTests.Value = action;
        return new TempCreateScope(previous);
    }

    internal static IDisposable UseBeforeDirectoryCreateForTests(Action<string> action)
    {
        var previous = BeforeDirectoryCreateForTests.Value;
        BeforeDirectoryCreateForTests.Value = action;
        return new DirectoryCreateScope(previous);
    }

    internal static void InvokeBeforeDirectoryCreateForTests(string path)
        => BeforeDirectoryCreateForTests.Value?.Invoke(path);

    private static void EnsureDirectWritePath(string rootFull, string fullPath)
    {
        var root = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetDirectoryName(fullPath);
        if (!PathsEqual(root, directory ?? ""))
        {
            throw Error(
                SandboxErrorCode.PermissionDenied,
                "file.writeText denied: nested write paths are not supported");
        }
    }

    private static string CreateTempSuffix()
        => TempSuffixFactory.Value?.Invoke() ?? Guid.NewGuid().ToString("N");

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class TempSuffixScope(Func<string>? previous) : IDisposable
    {
        public void Dispose() => TempSuffixFactory.Value = previous;
    }

    private sealed class TempCreateScope(Action? previous) : IDisposable
    {
        public void Dispose() => BeforeTempCreateForTests.Value = previous;
    }

    private sealed class DirectoryCreateScope(Action<string>? previous) : IDisposable
    {
        public void Dispose() => BeforeDirectoryCreateForTests.Value = previous;
    }
}
