namespace SafeIR.Hosting;

using System.Runtime.Loader;
using SafeIR.Compiler;

internal sealed class MaterializedCompiledArtifact(CompiledArtifact artifact, AssemblyLoadContext? loadContext) : IDisposable
{
    private int _disposed;

    public CompiledArtifact Artifact { get; } = artifact;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            loadContext?.Unload();
        }
    }
}
