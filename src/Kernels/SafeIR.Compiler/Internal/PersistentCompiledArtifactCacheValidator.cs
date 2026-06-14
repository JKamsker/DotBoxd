namespace SafeIR.Compiler.Internal;

using SafeIR;
using SafeIR.Verifier;

internal static class PersistentCompiledArtifactCacheValidator
{
    public static void ValidateCacheKey(string cacheKey)
    {
        if (cacheKey.Length != 64 || !cacheKey.All(Uri.IsHexDigit))
        {
            throw CacheInvalid("cache key is not path safe");
        }
    }

    public static void ValidateManifest(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        ArtifactManifest manifest,
        VerificationPolicy policy)
    {
        ValidateManifestIdentity(cacheKey, plan, manifest, policy);
        if (manifest.OptimizationFlags is null)
        {
            throw CacheInvalid("cached artifact optimization flags are missing");
        }

        var expectedFlags = ExpectedOptimizationFlags(cacheKey, plan, entrypoint, policy);
        if (!manifest.OptimizationFlags.SequenceEqual(expectedFlags, StringComparer.Ordinal))
        {
            throw CacheInvalid("cached artifact optimization flags do not match cache key");
        }
    }

    public static void ValidateVerification(
        ArtifactManifest manifest,
        VerificationResult verification,
        VerificationPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(verification.AssemblyHash) ||
            string.IsNullOrWhiteSpace(verification.VerifierVersion))
        {
            throw CacheInvalid("cached artifact verification metadata is incomplete");
        }

        if (!verification.Succeeded ||
            verification.VerifierVersion != policy.VerifierVersion ||
            verification.AssemblyHash != manifest.AssemblyHash)
        {
            throw CacheInvalid("cached artifact verification does not match current verifier");
        }
    }

    private static void ValidateManifestIdentity(
        string cacheKey,
        ExecutionPlan plan,
        ArtifactManifest manifest,
        VerificationPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(manifest.AssemblyHash))
        {
            throw CacheInvalid("cached artifact assembly hash is missing");
        }

        if (manifest.ArtifactVersion != 1 ||
            manifest.CacheKey != cacheKey ||
            manifest.ModuleHash != plan.ModuleHash ||
            manifest.PlanHash != plan.PlanHash ||
            manifest.PolicyHash != plan.PolicyHash ||
            manifest.BindingManifestHash != plan.BindingManifestHash ||
            manifest.CompilerVersion != CacheKeyBuilder.CompilerVersion ||
            manifest.TypeSystemVersion != CacheKeyBuilder.TypeSystemVersion ||
            manifest.EffectAnalysisVersion != CacheKeyBuilder.EffectAnalysisVersion ||
            manifest.VerifierVersion != policy.VerifierVersion ||
            manifest.RuntimeFacadeHash != policy.RuntimeFacadeHash ||
            manifest.LanguageVersion != CacheKeyBuilder.LanguageVersion ||
            manifest.TargetFramework != CacheKeyBuilder.TargetFramework)
        {
            throw CacheInvalid("cached artifact manifest does not match current plan");
        }
    }

    private static string[] ExpectedOptimizationFlags(
        string cacheKey,
        ExecutionPlan plan,
        string entrypoint,
        VerificationPolicy policy)
    {
        if (cacheKey == CacheKeyBuilder.Build(plan, entrypoint, policy, optimize: false))
        {
            return ["boxed-values"];
        }

        if (cacheKey == CacheKeyBuilder.Build(plan, entrypoint, policy, optimize: true))
        {
            return ["opt"];
        }

        throw CacheInvalid("cache key does not match current compile options");
    }

    private static SandboxRuntimeException CacheInvalid(string message)
        => new(new SandboxError(SandboxErrorCode.CacheInvalid, message));
}
