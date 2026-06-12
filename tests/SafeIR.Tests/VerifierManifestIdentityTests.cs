using System.Security.Cryptography;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierManifestIdentityTests
{
    [Theory]
    [InlineData("artifact")]
    [InlineData("cache")]
    [InlineData("module")]
    [InlineData("plan")]
    [InlineData("policy")]
    [InlineData("bindings")]
    [InlineData("runtime")]
    [InlineData("compiler")]
    [InlineData("type-system")]
    [InlineData("analysis")]
    [InlineData("verifier")]
    [InlineData("language")]
    [InlineData("target")]
    [InlineData("flags")]
    public async Task Direct_verifier_rejects_stale_manifest_identity(string staleField)
    {
        var policy = VerificationPolicy.BoxedValueDefaults();
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));
        var manifest = CurrentManifest(bytes, policy);
        var expected = VerificationManifestIdentity.FromManifest(manifest);

        var result = await new GeneratedAssemblyVerifier().VerifyAsync(
            bytes,
            StaleManifest(manifest, staleField),
            policy.WithExpectedManifest(expected),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-MANIFEST-IDENTITY");
    }

    [Fact]
    public async Task Direct_verifier_accepts_current_manifest_identity()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));
        var manifest = CurrentManifest(bytes, policy);

        var result = await new GeneratedAssemblyVerifier().VerifyAsync(
            bytes,
            manifest,
            policy.WithExpectedManifest(VerificationManifestIdentity.FromManifest(manifest)),
            CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public async Task Direct_verifier_rejects_missing_manifest_assembly_hash()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => VerifierTestHelpers.DefineValidExecute(type));
        var manifest = CurrentManifest(bytes, policy) with { AssemblyHash = "" };

        var result = await new GeneratedAssemblyVerifier().VerifyAsync(
            bytes,
            manifest,
            policy,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-MANIFEST-HASH");
    }

    private static ArtifactManifest CurrentManifest(byte[] bytes, VerificationPolicy policy)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ArtifactManifest(
            1,
            "cache",
            "module",
            "plan",
            "policy",
            "bindings",
            policy.RuntimeFacadeHash,
            "compiler",
            "type-system",
            "effect-analysis",
            policy.VerifierVersion,
            "1.0.0",
            "net10.0",
            ["boxed-values"],
            hash,
            DateTimeOffset.UtcNow);
    }

    private static ArtifactManifest StaleManifest(ArtifactManifest manifest, string field)
        => field switch
        {
            "artifact" => manifest with { ArtifactVersion = 0 },
            "cache" => manifest with { CacheKey = "stale-cache" },
            "module" => manifest with { ModuleHash = "stale-module" },
            "plan" => manifest with { PlanHash = "stale-plan" },
            "policy" => manifest with { PolicyHash = "stale-policy" },
            "bindings" => manifest with { BindingManifestHash = "stale-bindings" },
            "runtime" => manifest with { RuntimeFacadeHash = "stale-runtime" },
            "compiler" => manifest with { CompilerVersion = "stale-compiler" },
            "type-system" => manifest with { TypeSystemVersion = "stale-type-system" },
            "analysis" => manifest with { EffectAnalysisVersion = "stale-analysis" },
            "verifier" => manifest with { VerifierVersion = "stale-verifier" },
            "language" => manifest with { LanguageVersion = "0.0.0" },
            "target" => manifest with { TargetFramework = "net9.0" },
            "flags" => manifest with { OptimizationFlags = ["opt"] },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown manifest field.")
        };
}
