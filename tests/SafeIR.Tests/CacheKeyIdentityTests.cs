using SafeIR.Compiler;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class CacheKeyIdentityTests
{
    [Fact]
    public async Task Cache_key_changes_when_verifier_allowlist_changes()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
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
    public void Default_runtime_facade_hash_is_derived_from_verifier_policy()
    {
        var policy = VerificationPolicy.BoxedValueDefaults();

        Assert.Equal(policy.RuntimeFacadeHash, CacheKeyBuilder.RuntimeFacadeHash);
    }
}
