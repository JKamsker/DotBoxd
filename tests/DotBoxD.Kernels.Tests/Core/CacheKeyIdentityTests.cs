using System.Security.Cryptography;
using System.Text;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;

namespace DotBoxD.Kernels.Tests.Core;

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
                .Append("DotBoxD.Kernels.Runtime.CompiledRuntime.TestOnly(DotBoxD.Kernels.Sandbox.SandboxContext):System.Void")
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
            "dotboxd-cache-v1",
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
                .Append("DotBoxD.Kernels.Runtime, Version=999.0.0.0, Mvid=00000000000000000000000000000000")
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

        Assert.Contains(policy.RuntimeFacadeIdentities, i => i.StartsWith("DotBoxD.Kernels, Version=", StringComparison.Ordinal));
        Assert.Contains(policy.RuntimeFacadeIdentities, i => i.StartsWith("DotBoxD.Kernels.Runtime, Version=", StringComparison.Ordinal));
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
        const string forgedMember = "DotBoxD.Kernels.Runtime.CompiledRuntime.TestOnly(DotBoxD.Kernels.Sandbox.SandboxContext):System.Void";

        members.Add(forgedMember);

        Assert.Equal(allowlistHash, policy.AllowlistHash);
        Assert.Equal(runtimeFacadeHash, policy.RuntimeFacadeHash);
        Assert.False(policy.IsMemberAllowed(forgedMember));
        Assert.False(policy.AllowedMembers is HashSet<string>);
    }

    [Fact]
    public void Verification_policy_with_expression_invalidates_cached_hashes()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();
        var allowlistHash = policy.AllowlistHash;
        var runtimeFacadeHash = policy.RuntimeFacadeHash;
        var changedPolicy = policy with
        {
            AllowedMembers = policy.AllowedMembers
                .Append("DotBoxD.Kernels.Runtime.CompiledRuntime.TestOnly(DotBoxD.Kernels.Sandbox.SandboxContext):System.Void")
                .ToHashSet(StringComparer.Ordinal)
        };

        Assert.NotEqual(allowlistHash, changedPolicy.AllowlistHash);
        Assert.NotEqual(runtimeFacadeHash, changedPolicy.RuntimeFacadeHash);
    }

    private static string HashParts(params string[] parts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
}
