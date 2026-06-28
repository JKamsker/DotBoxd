using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Hosting.Execution;

internal static class CompiledArtifactGuard
{
    private static readonly VerificationPolicy DefaultVerificationPolicy = VerificationPolicy.BoxedValueDefaults();
    private static readonly IGeneratedAssemblyVerifier Verifier = new GeneratedAssemblyVerifier();

    // The boxed/optimized cache keys for a prepared plan and entrypoint are a pure, deterministic
    // function of the plan identity, the entrypoint, and the static default verification policy.
    // Memoize them so steady-state dispatches do not rebuild and re-hash both cache-key strings on
    // every compiled run. Keyed by plan identity (PlanHash folds the module/policy/binding hashes
    // and determinism flag that CacheKeyBuilder.Build reads) plus the entrypoint.
    private static readonly ConcurrentDictionary<(string PlanHash, string Entrypoint), ExpectedCacheKeys> ExpectedCacheKeyCache =
        new();

    public static async ValueTask<MaterializedCompiledArtifact> MaterializeExecutableAsync(
        CompiledArtifact artifact,
        ExecutionPlan plan,
        string entrypoint,
        CancellationToken cancellationToken)
    {
        ValidateExecutableEnvelope(artifact, plan, entrypoint);
        if (artifact.RuntimeForm == CompiledRuntimeFormKind.DynamicMethod)
        {
            throw Invalid("dynamic method artifacts require an independent verifier gate before execution");
        }

        var assemblyBytes = artifact.AssemblyBytesUnsafe;
        var verification = await Verifier
            .VerifyAsync(
                artifact.AssemblyBytesMemory,
                artifact.Manifest,
                DefaultVerificationPolicy.WithExpectedManifest(VerificationManifestIdentity.FromManifest(artifact.Manifest)),
                cancellationToken)
            .ConfigureAwait(false);
        if (!verification.Succeeded)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.VerifierFailure,
                "compiled artifact failed verification"));
        }

        if (!StringComparer.Ordinal.Equals(artifact.AssemblyHash, verification.AssemblyHash))
        {
            throw Invalid("compiled artifact bytes do not match artifact hash");
        }

        var loaded = LoadEntrypoint(assemblyBytes, artifact.AssemblyHash);
        return new MaterializedCompiledArtifact(new CompiledArtifact(
            assemblyBytes,
            artifact.AssemblyHash,
            artifact.Manifest,
            verification,
            loaded.Entrypoint,
            CompiledRuntimeFormKind.LoadedAssembly,
            artifact.CacheStatus,
            artifact.CacheInvalidReason,
            copyAssemblyBytes: false),
            loaded.Context);
    }

    public static void ValidateExecutableEnvelope(CompiledArtifact artifact, ExecutionPlan plan, string entrypoint)
    {
        EnsureMatchesPlan(artifact, plan, entrypoint);
        EnsureAssemblyBytesMatchHash(artifact);
    }

    public static void ValidateCachedExecutableEnvelope(CompiledArtifact artifact, ExecutionPlan plan, string entrypoint)
        => EnsureMatchesPlan(artifact, plan, entrypoint);

    private static void EnsureMatchesPlan(CompiledArtifact artifact, ExecutionPlan plan, string entrypoint)
    {
        var manifest = artifact.Manifest;
        if (!Enum.IsDefined(artifact.RuntimeForm))
        {
            throw Invalid("compiled artifact runtime form is not supported");
        }

        if (!artifact.Verification.Succeeded ||
            !StringComparer.Ordinal.Equals(artifact.AssemblyHash, artifact.Verification.AssemblyHash) ||
            !StringComparer.Ordinal.Equals(artifact.Verification.VerifierVersion, DefaultVerificationPolicy.VerifierVersion) ||
            !StringComparer.Ordinal.Equals(artifact.AssemblyHash, manifest.AssemblyHash))
        {
            throw Invalid("compiled artifact verification does not match artifact hash");
        }

        if (artifact.RuntimeForm == CompiledRuntimeFormKind.LoadedAssembly && artifact.AssemblyBytesMemory.Length == 0)
        {
            throw Invalid("loaded compiled artifact did not include assembly bytes");
        }

        if (artifact.RuntimeForm == CompiledRuntimeFormKind.DynamicMethod && artifact.AssemblyBytesMemory.Length != 0)
        {
            throw Invalid("dynamic method artifact must not include assembly bytes");
        }

        if (manifest.ArtifactVersion != 1 ||
            manifest.ModuleHash != plan.ModuleHash ||
            manifest.PlanHash != plan.PlanHash ||
            manifest.PolicyHash != plan.PolicyHash ||
            manifest.BindingManifestHash != plan.BindingManifestHash ||
            manifest.CompilerVersion != CacheKeyBuilder.CompilerVersion ||
            manifest.TypeSystemVersion != CacheKeyBuilder.TypeSystemVersion ||
            manifest.EffectAnalysisVersion != CacheKeyBuilder.EffectAnalysisVersion ||
            manifest.VerifierVersion != DefaultVerificationPolicy.VerifierVersion ||
            manifest.RuntimeFacadeHash != DefaultVerificationPolicy.RuntimeFacadeHash ||
            manifest.LanguageVersion != CacheKeyBuilder.LanguageVersion ||
            manifest.TargetFramework != CacheKeyBuilder.TargetFramework)
        {
            throw Invalid("compiled artifact manifest does not match execution plan");
        }

        var expectedFlag = ExpectedOptimizationFlag(manifest.CacheKey, plan, entrypoint);
        if (manifest.OptimizationFlags is not { Count: 1 } optimizationFlags ||
            !StringComparer.Ordinal.Equals(optimizationFlags[0], expectedFlag))
        {
            throw Invalid("compiled artifact optimization flags do not match cache key");
        }
    }

    private static string ExpectedOptimizationFlag(string cacheKey, ExecutionPlan plan, string entrypoint)
    {
        var expected = ExpectedKeysFor(plan, entrypoint);
        if (cacheKey == expected.BoxedValues)
        {
            return "boxed-values";
        }

        if (cacheKey == expected.Optimized)
        {
            return "opt";
        }

        throw Invalid("compiled artifact cache key does not match execution plan");
    }

    private static ExpectedCacheKeys ExpectedKeysFor(ExecutionPlan plan, string entrypoint)
        => ExpectedCacheKeyCache.GetOrAdd(
            (plan.PlanHash, entrypoint),
            static (_, state) => new ExpectedCacheKeys(
                CacheKeyBuilder.Build(state.Plan, state.Entrypoint, DefaultVerificationPolicy, optimize: false),
                CacheKeyBuilder.Build(state.Plan, state.Entrypoint, DefaultVerificationPolicy, optimize: true)),
            (Plan: plan, Entrypoint: entrypoint));

    private readonly record struct ExpectedCacheKeys(string BoxedValues, string Optimized);

    private static void EnsureAssemblyBytesMatchHash(CompiledArtifact artifact)
    {
        var actual = Convert.ToHexString(SHA256.HashData(artifact.AssemblyBytesMemory.Span)).ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(actual, artifact.AssemblyHash))
        {
            throw Invalid("compiled artifact bytes do not match artifact hash");
        }
    }

    private static (SandboxCompiledEntrypoint Entrypoint, AssemblyLoadContext Context) LoadEntrypoint(
        byte[] assemblyBytes,
        string assemblyHash)
    {
        var context = new CompiledArtifactLoadContext(assemblyHash);
        var assembly = context.LoadFromStream(new MemoryStream(assemblyBytes, writable: false));
        var type = assembly.GetTypes().Single(t => t.Name.StartsWith("Module_", StringComparison.Ordinal));
        var method = type.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static) ??
            throw new MissingMethodException(type.FullName, "Execute");
        EnsureEntrypointSignature(method);
        return (method.CreateDelegate<SandboxCompiledEntrypoint>(), context);
    }

    private static void EnsureEntrypointSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (method.ReturnType != typeof(SandboxValue) ||
            parameters.Length != 2 ||
            parameters[0].ParameterType != typeof(SandboxContext) ||
            parameters[1].ParameterType != typeof(SandboxValue))
        {
            throw Invalid("compiled artifact entrypoint does not bind to the current runtime facade");
        }
    }

    private sealed class CompiledArtifactLoadContext(string assemblyHash)
        : AssemblyLoadContext("DotBoxD.Kernels.Generated.Host." + assemblyHash, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (MatchesCurrentFacadeAssembly(assemblyName, typeof(SandboxValue).Assembly, out var core))
            {
                return core;
            }

            if (MatchesCurrentFacadeAssembly(assemblyName, typeof(CompiledRuntime).Assembly, out var runtime))
            {
                return runtime;
            }

            return null;
        }

        private static bool MatchesCurrentFacadeAssembly(
            AssemblyName requested,
            Assembly current,
            out Assembly? resolved)
        {
            var currentName = current.GetName();
            if (!string.Equals(requested.Name, currentName.Name, StringComparison.Ordinal))
            {
                resolved = null;
                return false;
            }

            if (!AssemblyName.ReferenceMatchesDefinition(requested, currentName))
            {
                throw Invalid("compiled artifact runtime facade reference does not match the current assembly");
            }

            resolved = current;
            return true;
        }
    }

    private static SandboxRuntimeException Invalid(string message)
        => new(new SandboxError(SandboxErrorCode.ValidationError, message));
}
