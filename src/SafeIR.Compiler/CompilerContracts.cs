namespace SafeIR.Compiler;

using SafeIR;
using SafeIR.Verifier;

public delegate SandboxValue SandboxCompiledEntrypoint(SandboxContext context, SandboxValue input);

public sealed record CompileOptions(string Entrypoint, bool Optimize = false);

public enum CompiledRuntimeFormKind
{
    LoadedAssembly,
    DynamicMethod
}

public enum CompiledCacheStatus
{
    None,
    Hit,
    Miss,
    Invalid,
    Recompiled
}

public sealed record CompiledArtifact
{
    public CompiledArtifact(
        byte[] assemblyBytes,
        string assemblyHash,
        ArtifactManifest manifest,
        VerificationResult verification,
        SandboxCompiledEntrypoint entrypoint,
        CompiledRuntimeFormKind runtimeForm,
        CompiledCacheStatus cacheStatus = CompiledCacheStatus.None)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(assemblyHash);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(entrypoint);

        if (runtimeForm == CompiledRuntimeFormKind.DynamicMethod && assemblyBytes.Length != 0) {
            throw new ArgumentException(
                "DynamicMethod artifacts expose only the created delegate, not assembly bytes.",
                nameof(AssemblyBytes));
        }

        if (runtimeForm == CompiledRuntimeFormKind.LoadedAssembly && assemblyBytes.Length == 0) {
            throw new ArgumentException(
                "LoadedAssembly artifacts must include the verified assembly image used to create the delegate.",
                nameof(AssemblyBytes));
        }

        AssemblyBytes = assemblyBytes;
        AssemblyHash = assemblyHash;
        Manifest = manifest;
        Verification = verification;
        Entrypoint = entrypoint;
        RuntimeForm = runtimeForm;
        CacheStatus = cacheStatus;
    }

    public byte[] AssemblyBytes { get; init; }
    public string AssemblyHash { get; init; }
    public ArtifactManifest Manifest { get; init; }
    public VerificationResult Verification { get; init; }
    public SandboxCompiledEntrypoint Entrypoint { get; init; }
    public CompiledRuntimeFormKind RuntimeForm { get; init; }
    public CompiledCacheStatus CacheStatus { get; init; }
    public string ArtifactHash => AssemblyHash;
}

public interface ISandboxCompiler
{
    ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken);
}
