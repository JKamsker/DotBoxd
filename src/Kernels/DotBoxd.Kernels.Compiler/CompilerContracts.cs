namespace DotBoxd.Kernels.Compiler;

using DotBoxd.Kernels;
using DotBoxd.Kernels.Verifier;

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
    private byte[] _assemblyBytes = [];

    public CompiledArtifact(
        byte[] assemblyBytes,
        string assemblyHash,
        ArtifactManifest manifest,
        VerificationResult verification,
        SandboxCompiledEntrypoint entrypoint,
        CompiledRuntimeFormKind runtimeForm,
        CompiledCacheStatus cacheStatus = CompiledCacheStatus.None,
        string? cacheInvalidReason = null)
        : this(
            assemblyBytes,
            assemblyHash,
            manifest,
            verification,
            entrypoint,
            runtimeForm,
            cacheStatus,
            cacheInvalidReason,
            copyAssemblyBytes: true)
    {
    }

    internal CompiledArtifact(
        byte[] assemblyBytes,
        string assemblyHash,
        ArtifactManifest manifest,
        VerificationResult verification,
        SandboxCompiledEntrypoint entrypoint,
        CompiledRuntimeFormKind runtimeForm,
        CompiledCacheStatus cacheStatus,
        string? cacheInvalidReason,
        bool copyAssemblyBytes)
    {
        ArgumentNullException.ThrowIfNull(assemblyBytes);
        ArgumentNullException.ThrowIfNull(assemblyHash);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(entrypoint);

        if (!Enum.IsDefined(runtimeForm))
        {
            throw new ArgumentOutOfRangeException(nameof(runtimeForm), runtimeForm, "Compiled runtime form is not supported.");
        }

        if (!verification.Succeeded)
        {
            throw new ArgumentException("Compiled runtime form must be verified or gated before execution.", nameof(verification));
        }

        if (!StringComparer.Ordinal.Equals(assemblyHash, verification.AssemblyHash) ||
            !StringComparer.Ordinal.Equals(assemblyHash, manifest.AssemblyHash))
        {
            throw new ArgumentException("Compiled artifact hash must match its manifest and verification result.", nameof(assemblyHash));
        }

        if (runtimeForm == CompiledRuntimeFormKind.DynamicMethod && assemblyBytes.Length != 0)
        {
            throw new ArgumentException(
                "DynamicMethod artifacts expose only the created delegate, not assembly bytes.",
                nameof(AssemblyBytes));
        }

        if (runtimeForm == CompiledRuntimeFormKind.LoadedAssembly && assemblyBytes.Length == 0)
        {
            throw new ArgumentException(
                "LoadedAssembly artifacts must include the verified assembly image used to create the delegate.",
                nameof(AssemblyBytes));
        }

        _assemblyBytes = copyAssemblyBytes ? assemblyBytes.ToArray() : assemblyBytes;
        AssemblyHash = assemblyHash;
        Manifest = manifest;
        Verification = verification;
        Entrypoint = entrypoint;
        RuntimeForm = runtimeForm;
        CacheStatus = cacheStatus;
        CacheInvalidReason = cacheInvalidReason;
    }

    public byte[] AssemblyBytes
    {
        get => _assemblyBytes.ToArray();
        init => _assemblyBytes = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
    internal ReadOnlyMemory<byte> AssemblyBytesMemory => _assemblyBytes;
    internal byte[] AssemblyBytesUnsafe => _assemblyBytes;
    public string AssemblyHash { get; init; }
    public ArtifactManifest Manifest { get; init; }
    public VerificationResult Verification { get; init; }
    public SandboxCompiledEntrypoint Entrypoint { get; init; }
    public CompiledRuntimeFormKind RuntimeForm { get; init; }
    public CompiledCacheStatus CacheStatus { get; init; }
    public string? CacheInvalidReason { get; init; }
    public string ArtifactHash => AssemblyHash;
}

public interface ISandboxCompiler
{
    ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken);
}
