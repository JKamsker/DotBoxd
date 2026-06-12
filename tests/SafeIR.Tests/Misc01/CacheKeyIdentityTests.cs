using System.Security.Cryptography;
using System.Text;
using SafeIR.Compiler;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class CacheKeyIdentityTests
{
    [Fact]
    public async Task Cache_key_changes_when_verifier_allowlist_changes()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var policy = VerificationPolicy.BoxedValueDefaults();
        var extendedPolicy = policy with
        {
            AllowedMembers = policy.AllowedMembers
                .Append("SafeIR.Runtime.CompiledRuntime.TestOnly(SafeIR.SandboxContext):System.Void")
                .ToHashSet(StringComparer.Ordinal)
        };

        Assert.NotEqual(
            CacheKeyBuilder.Build(plan, "main", policy, optimize: false),
            CacheKeyBuilder.Build(plan, "main", extendedPolicy, optimize: false));
    }

    [Fact]
    public async Task Cache_key_identity_includes_canonicalizer_version()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var policy = VerificationPolicy.BoxedValueDefaults();

        var expected = HashParts(
            "safe-ir-cache-v1",
            plan.ModuleHash,
            CacheKeyBuilder.CanonicalizerVersion,
            "main",
            CacheKeyBuilder.LanguageVersion,
            CacheKeyBuilder.CompilerVersion,
            CacheKeyBuilder.TypeSystemVersion,
            CacheKeyBuilder.EffectAnalysisVersion,
            policy.VerifierVersion,
            policy.AllowlistHash,
            policy.RuntimeFacadeHash,
            plan.BindingManifestHash,
            plan.PolicyHash,
            CacheKeyBuilder.TargetFramework,
            "boxed-values",
            plan.Policy.Deterministic ? "deterministic" : "nondeterministic");

        Assert.Equal(CanonicalModuleHasher.CanonicalizerVersion, CacheKeyBuilder.CanonicalizerVersion);
        Assert.Equal(expected, CacheKeyBuilder.Build(plan, "main", policy, optimize: false));
    }

    [Fact]
    public void Default_runtime_facade_hash_is_derived_from_verifier_policy()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();

        Assert.Equal(policy.RuntimeFacadeHash, CacheKeyBuilder.RuntimeFacadeHash);
    }

    [Fact]
    public async Task Cache_key_changes_when_runtime_facade_identity_changes()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var policy = VerificationPolicy.BoxedValueDefaults();
        var changedPolicy = policy with
        {
            RuntimeFacadeIdentities = policy.RuntimeFacadeIdentities
                .Append("SafeIR.Runtime, Version=999.0.0.0, Mvid=00000000000000000000000000000000")
                .ToHashSet(StringComparer.Ordinal)
        };

        Assert.NotEqual(policy.RuntimeFacadeHash, changedPolicy.RuntimeFacadeHash);
        Assert.NotEqual(
            CacheKeyBuilder.Build(plan, "main", policy, optimize: false),
            CacheKeyBuilder.Build(plan, "main", changedPolicy, optimize: false));
    }

    [Fact]
    public void Default_runtime_facade_identity_includes_loaded_core_and_runtime_modules()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();

        Assert.Contains(policy.RuntimeFacadeIdentities, i => i.StartsWith("SafeIR.Core, Version=", StringComparison.Ordinal));
        Assert.Contains(policy.RuntimeFacadeIdentities, i => i.StartsWith("SafeIR.Runtime, Version=", StringComparison.Ordinal));
        Assert.All(policy.RuntimeFacadeIdentities, i => Assert.Contains(", Mvid=", i, StringComparison.Ordinal));
    }

    [Fact]
    public void Verification_policy_copies_allowlist_inputs()
    {
        var members = VerificationPolicy.BoxedValueDefaults()
            .AllowedMembers
            .ToHashSet(StringComparer.Ordinal);
        var policy = VerificationPolicy.BoxedValueDefaults() with { AllowedMembers = members };
        var allowlistHash = policy.AllowlistHash;
        var runtimeFacadeHash = policy.RuntimeFacadeHash;
        const string forgedMember = "SafeIR.Runtime.CompiledRuntime.TestOnly(SafeIR.SandboxContext):System.Void";

        members.Add(forgedMember);

        Assert.Equal(allowlistHash, policy.AllowlistHash);
        Assert.Equal(runtimeFacadeHash, policy.RuntimeFacadeHash);
        Assert.False(policy.IsMemberAllowed(forgedMember));
        Assert.False(policy.AllowedMembers is HashSet<string>);
    }

    private static string HashParts(params string[] parts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
}
