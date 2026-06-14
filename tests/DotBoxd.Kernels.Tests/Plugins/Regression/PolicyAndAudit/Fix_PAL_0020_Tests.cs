namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0020: <see cref="SandboxPolicy.Hash"/> recomputed the full
/// canonical policy hash (reallocating canonical grant/parameter records) on every property
/// access, and the prepare/execute plan paths read it several times per plan build.
///
/// The fix caches the computed hash in a thread-safe lazy field so a reused policy instance
/// builds the canonical representation at most once. These tests pin the observable behaviour
/// that must be preserved: repeated reads return the identical cached value, the cached value
/// equals a fresh canonical computation, and any state-changing <c>with</c> copy recomputes the
/// hash to reflect the new policy state (it must never alias the original policy's hash).
/// </summary>
public sealed class Fix_PAL_0020_Tests
{
    private static SandboxPolicy PolicyWithGrant(CapabilityGrant grant)
        => new(
            "pal-0020",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [grant],
            new ResourceLimits(MaxFuel: 1_000));

    private static CapabilityGrant FileReadGrant(string root)
        => new(
            "file.read",
            new Dictionary<string, string>
            {
                ["root"] = root,
                ["maxBytesPerRun"] = "1024"
            });

    [Fact]
    public void Repeated_hash_reads_return_the_same_cached_value()
    {
        var policy = PolicyWithGrant(FileReadGrant("alpha"));

        var first = policy.Hash;
        var second = policy.Hash;
        var third = policy.Hash;

        // A recompute would build a brand-new string each time; the cache returns one instance.
        Assert.Same(first, second);
        Assert.Same(second, third);
    }

    [Fact]
    public void Cached_hash_equals_a_fresh_canonical_computation()
    {
        var policy = PolicyWithGrant(FileReadGrant("alpha"));

        // Behaviour preserved: the cached value is exactly what the canonical hasher produces.
        Assert.Equal(PolicyHash.Compute(policy), policy.Hash);
    }

    [Fact]
    public void With_changing_grants_recomputes_and_does_not_alias_original_hash()
    {
        var policy = PolicyWithGrant(FileReadGrant("alpha"));
        var originalHash = policy.Hash;

        var updated = policy with { Grants = new[] { FileReadGrant("beta") } };

        Assert.NotEqual(originalHash, updated.Hash);
        Assert.Equal(PolicyHash.Compute(updated), updated.Hash);
        // The original instance keeps its own cached value untouched.
        Assert.Equal(originalHash, policy.Hash);
    }

    [Fact]
    public void With_changing_resource_limits_recomputes_the_hash()
    {
        var policy = PolicyWithGrant(FileReadGrant("alpha"));
        var originalHash = policy.Hash;

        var updated = policy with { ResourceLimits = new ResourceLimits(MaxFuel: 2_000) };

        Assert.NotEqual(originalHash, updated.Hash);
        Assert.Equal(PolicyHash.Compute(updated), updated.Hash);
    }

    [Fact]
    public void With_changing_scalar_policy_state_recomputes_the_hash()
    {
        var policy = PolicyWithGrant(FileReadGrant("alpha"));
        var originalHash = policy.Hash;

        var renamed = policy with { PolicyId = "pal-0020-renamed" };
        var redetermined = policy with { Deterministic = true };
        var reseeded = policy with { RandomSeed = 7UL };

        Assert.NotEqual(originalHash, renamed.Hash);
        Assert.NotEqual(originalHash, redetermined.Hash);
        Assert.NotEqual(originalHash, reseeded.Hash);
        Assert.Equal(PolicyHash.Compute(renamed), renamed.Hash);
        Assert.Equal(PolicyHash.Compute(redetermined), redetermined.Hash);
        Assert.Equal(PolicyHash.Compute(reseeded), reseeded.Hash);
    }

    [Fact]
    public void Independent_instances_with_equal_state_produce_equal_hashes()
    {
        var left = PolicyWithGrant(FileReadGrant("alpha"));
        var right = PolicyWithGrant(FileReadGrant("alpha"));

        // Touch one cache but not the other; equal policy state must still hash identically.
        _ = left.Hash;

        Assert.Equal(left.Hash, right.Hash);
    }
}
