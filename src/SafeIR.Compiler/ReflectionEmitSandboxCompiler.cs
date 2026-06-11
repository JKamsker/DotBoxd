namespace SafeIR.Compiler;

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using SafeIR;
using SafeIR.Verifier;

public sealed class ReflectionEmitSandboxCompiler : ISandboxCompiler
{
    private readonly IGeneratedAssemblyVerifier _verifier;
    private readonly VerificationPolicy _verificationPolicy;
    private readonly PersistentCompiledArtifactCache? _cache;

    public ReflectionEmitSandboxCompiler(
        IGeneratedAssemblyVerifier verifier,
        VerificationPolicy? verificationPolicy = null,
        PersistentCompiledArtifactCache? cache = null)
    {
        _verifier = verifier;
        _verificationPolicy = verificationPolicy ?? VerificationPolicy.BoxedValueDefaults();
        _cache = cache;
    }

    public async ValueTask<CompiledArtifact> CompileAsync(
        ExecutionPlan plan,
        CompileOptions options,
        CancellationToken cancellationToken)
    {
        var function = ResolveSupportedFunction(plan, options.Entrypoint);
        var cacheKey = CacheKeyBuilder.Build(plan, _verificationPolicy, options.Optimize);
        var lookupStatus = CompiledCacheStatus.None;
        if (_cache is not null) {
            var cached = await _cache.TryReadAsync(
                cacheKey,
                plan,
                _verifier,
                _verificationPolicy,
                cancellationToken).ConfigureAwait(false);
            if (cached.Status == CompiledCacheStatus.Hit && cached.Artifact is not null) {
                return cached.Artifact with {
                    Entrypoint = LoadEntrypoint(cached.Artifact.AssemblyBytes),
                    CacheStatus = CompiledCacheStatus.Hit
                };
            }

            lookupStatus = cached.Status;
        }

        var assemblyBytes = EmitAssembly(plan, function);
        var manifest = BuildManifest(plan, assemblyBytes, options);
        var verification = await _verifier.VerifyAsync(assemblyBytes, manifest, _verificationPolicy, cancellationToken).ConfigureAwait(false);
        if (!verification.Succeeded) {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.VerifierFailure,
                "compiled artifact failed verification"));
        }

        var entrypoint = LoadEntrypoint(assemblyBytes);
        var status = _cache is null
            ? CompiledCacheStatus.None
            : await WriteCacheAsync(cacheKey, lookupStatus, plan, assemblyBytes, manifest, verification, cancellationToken).ConfigureAwait(false);
        return new CompiledArtifact(assemblyBytes, verification.AssemblyHash, manifest, verification, entrypoint, status);
    }

    private static SandboxFunction ResolveSupportedFunction(ExecutionPlan plan, string entrypoint)
    {
        var function = plan.Module.Functions.FirstOrDefault(f => f.Id == entrypoint && f.IsEntrypoint);
        if (function is null) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, $"entrypoint '{entrypoint}' is not available"));
        }

        var effects = plan.FunctionAnalysis[function.Id].Effects;
        if ((effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) != SandboxEffect.None) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "compiled mode supports pure modules only"));
        }

        if (plan.Module.Functions.Count > 1) {
            throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "compiled mode does not support internal calls yet"));
        }

        return function;
    }

    private static byte[] EmitAssembly(ExecutionPlan plan, SandboxFunction function)
    {
        var assemblyName = new AssemblyName("SafeIR.Generated." + plan.ModuleHash[..16]);
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("SafeIR.Generated.Module");
        var type = module.DefineType("SafeIR.Generated.Module_" + plan.ModuleHash[..16], TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
        var method = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

        new MethodEmitter(method.GetILGenerator(), function).Emit();
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private ArtifactManifest BuildManifest(ExecutionPlan plan, byte[] assemblyBytes, CompileOptions options)
    {
        var assemblyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(assemblyBytes)).ToLowerInvariant();
        return new ArtifactManifest(
            1,
            CacheKeyBuilder.Build(plan, _verificationPolicy, options.Optimize),
            plan.ModuleHash,
            plan.PlanHash,
            plan.PolicyHash,
            plan.BindingManifestHash,
            CacheKeyBuilder.RuntimeFacadeHash,
            CacheKeyBuilder.CompilerVersion,
            _verificationPolicy.VerifierVersion,
            CacheKeyBuilder.LanguageVersion,
            CacheKeyBuilder.TargetFramework,
            [options.Optimize ? "opt" : "boxed-values"],
            assemblyHash,
            DateTimeOffset.UtcNow);
    }

    private async ValueTask<CompiledCacheStatus> WriteCacheAsync(
        string cacheKey,
        CompiledCacheStatus lookupStatus,
        ExecutionPlan plan,
        byte[] assemblyBytes,
        ArtifactManifest manifest,
        VerificationResult verification,
        CancellationToken cancellationToken)
    {
        var cache = _cache ?? throw new InvalidOperationException("compiler cache is not configured");
        var existing = lookupStatus == CompiledCacheStatus.Invalid || cache.EntryExists(cacheKey)
            ? CompiledCacheStatus.Recompiled
            : CompiledCacheStatus.Miss;
        await cache.WriteAsync(cacheKey, plan, assemblyBytes, manifest, verification, cancellationToken).ConfigureAwait(false);
        return existing;
    }

    internal static SandboxCompiledEntrypoint LoadEntrypoint(byte[] assemblyBytes)
    {
        var context = new AssemblyLoadContext("SafeIR.Generated", isCollectible: true);
        var assembly = context.LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
        var type = assembly.GetTypes().Single(t => t.Name.StartsWith("Module_", StringComparison.Ordinal));
        var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static) ??
            throw new MissingMethodException(type.FullName, "Execute");
        return method.CreateDelegate<SandboxCompiledEntrypoint>();
    }
}
