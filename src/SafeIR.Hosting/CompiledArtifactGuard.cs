namespace SafeIR.Hosting;

using System.Reflection;
using System.Runtime.Loader;
using SafeIR;
using SafeIR.Compiler;
using SafeIR.Verifier;

internal static class CompiledArtifactGuard
{
    private static readonly VerificationPolicy DefaultVerificationPolicy = VerificationPolicy.BoxedValueDefaults();
    private static readonly IGeneratedAssemblyVerifier Verifier = new GeneratedAssemblyVerifier();

    public static async ValueTask<CompiledArtifact> MaterializeExecutableAsync(
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        EnsureMatchesPlan(artifact, plan, entrypoint);
        var assemblyBytes = artifact.AssemblyBytes.ToArray();
        var verification = await Verifier.VerifyAsync(assemblyBytes, artifact.Manifest, DefaultVerificationPolicy, cancellationToken)
            .ConfigureAwait(false);
        if (!verification.Succeeded) {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.VerifierFailure,
                "compiled artifact failed verification"));
        }

        if (!StringComparer.Ordinal.Equals(artifact.AssemblyHash, verification.AssemblyHash)) {
            throw Invalid("compiled artifact bytes do not match artifact hash");
        }

        return new CompiledArtifact(
            assemblyBytes,
            artifact.AssemblyHash,
            artifact.Manifest,
            verification,
            LoadEntrypoint(assemblyBytes),
            CompiledRuntimeFormKind.LoadedAssembly,
            artifact.CacheStatus);
    }

    private static void EnsureMatchesPlan(CompiledArtifact artifact, ExecutionPlan plan, string entrypoint)
    {
        var manifest = artifact.Manifest;
        if (!Enum.IsDefined(artifact.RuntimeForm)) {
            throw Invalid("compiled artifact runtime form is not supported");
        }

        if (artifact.RuntimeForm != CompiledRuntimeFormKind.LoadedAssembly) {
            throw Invalid("compiled artifact must expose verifiable loaded assembly bytes");
        }

        if (artifact.AssemblyBytes.Length == 0) {
            throw Invalid("loaded compiled artifact did not include assembly bytes");
        }

        if (!artifact.Verification.Succeeded ||
            !StringComparer.Ordinal.Equals(artifact.AssemblyHash, artifact.Verification.AssemblyHash) ||
            !StringComparer.Ordinal.Equals(artifact.AssemblyHash, manifest.AssemblyHash)) {
            throw Invalid("compiled artifact verification does not match artifact hash");
        }

        if (manifest.ArtifactVersion != 1 ||
            manifest.ModuleHash != plan.ModuleHash ||
            manifest.PlanHash != plan.PlanHash ||
            manifest.PolicyHash != plan.PolicyHash ||
            manifest.BindingManifestHash != plan.BindingManifestHash ||
            manifest.CompilerVersion != CacheKeyBuilder.CompilerVersion ||
            manifest.VerifierVersion != DefaultVerificationPolicy.VerifierVersion ||
            manifest.RuntimeFacadeHash != CacheKeyBuilder.RuntimeFacadeHash ||
            manifest.LanguageVersion != CacheKeyBuilder.LanguageVersion ||
            manifest.TargetFramework != CacheKeyBuilder.TargetFramework) {
            throw Invalid("compiled artifact manifest does not match execution plan");
        }

        var expectedFlags = ExpectedOptimizationFlags(manifest.CacheKey, plan, entrypoint);
        if (manifest.OptimizationFlags is null ||
            !manifest.OptimizationFlags.SequenceEqual(expectedFlags, StringComparer.Ordinal)) {
            throw Invalid("compiled artifact optimization flags do not match cache key");
        }
    }

    private static string[] ExpectedOptimizationFlags(string cacheKey, ExecutionPlan plan, string entrypoint)
    {
        if (cacheKey == CacheKeyBuilder.Build(plan, entrypoint, DefaultVerificationPolicy, optimize: false)) {
            return ["boxed-values"];
        }

        if (cacheKey == CacheKeyBuilder.Build(plan, entrypoint, DefaultVerificationPolicy, optimize: true)) {
            return ["opt"];
        }

        throw Invalid("compiled artifact cache key does not match execution plan");
    }

    private static SandboxCompiledEntrypoint LoadEntrypoint(byte[] assemblyBytes)
    {
        var context = new AssemblyLoadContext("SafeIR.Generated.Host", isCollectible: true);
        var assembly = context.LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
        var type = assembly.GetTypes().Single(t => t.Name.StartsWith("Module_", StringComparison.Ordinal));
        var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static) ??
            throw new MissingMethodException(type.FullName, "Execute");
        return method.CreateDelegate<SandboxCompiledEntrypoint>();
    }

    private static SandboxRuntimeException Invalid(string message)
        => new(new SandboxError(SandboxErrorCode.ValidationError, message));
}
