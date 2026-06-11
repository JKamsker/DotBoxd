namespace SafeIR.Compiler;

using SafeIR;
using SafeIR.Verifier;

public delegate SandboxValue SandboxCompiledEntrypoint(SandboxContext context, SandboxValue input);

public sealed record CompileOptions(string Entrypoint, bool Optimize = false);

public enum CompiledCacheStatus
{
    None,
    Hit,
    Miss,
    Invalid,
    Recompiled
}

public sealed record CompiledArtifact(
    byte[] AssemblyBytes,
    string AssemblyHash,
    ArtifactManifest Manifest,
    VerificationResult Verification,
    SandboxCompiledEntrypoint Entrypoint,
    CompiledCacheStatus CacheStatus = CompiledCacheStatus.None);

public interface ISandboxCompiler
{
    ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken);
}
