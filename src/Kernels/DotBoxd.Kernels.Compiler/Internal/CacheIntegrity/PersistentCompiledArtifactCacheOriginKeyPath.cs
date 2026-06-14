namespace DotBoxd.Kernels.Compiler.Internal;

using DotBoxd.Kernels;

internal static class PersistentCompiledArtifactCacheOriginKeyPath
{
    private static readonly AsyncLocal<string?> Override = new();

    public static IDisposable UseForCurrentAsyncFlow(string keyPath)
    {
        var previous = Override.Value;
        Override.Value = Path.GetFullPath(keyPath);
        return new OverrideScope(previous);
    }

    public static string Get()
    {
        if (!string.IsNullOrWhiteSpace(Override.Value))
        {
            return Override.Value;
        }

        var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.CacheInvalid,
                "compiled cache origin key directory is unavailable"));
        }

        return Path.Combine(baseDirectory, "DotBoxd.Kernels", "compiled-cache-origin.key");
    }

    private sealed class OverrideScope(string? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
