namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for ALG-0009: capability lookups must resolve through a single
/// immutable per-policy index instead of rescanning the whole grant list on every
/// <see cref="SandboxPolicy.GrantsCapability(string)"/>/<see cref="SandboxPolicy.GetGrant"/> call.
/// These tests pin the observable semantics the index must preserve exactly:
/// first-match-by-list-order resolution, call-time expiry evaluation against
/// <c>GrantClock</c>, fail-closed denial, and index rebuild on <c>with</c> copies.
/// </summary>
public sealed class Fix_ALG_0009_Tests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 0, 0, 0, TimeSpan.Zero);

    private static CapabilityGrant Grant(string id, DateTimeOffset? expiresAt = null)
        => new(id, new Dictionary<string, string>(), expiresAt);

    private static SandboxPolicy DeterministicPolicy(params CapabilityGrant[] grants)
        => new(
            "policy",
            SandboxEffect.Audit,
            grants,
            new ResourceLimits(),
            Deterministic: true,
            LogicalNow: Now,
            RandomSeed: 1);

    [Fact]
    public void GetGrant_returns_first_matching_grant_in_list_order()
    {
        // Two active grants share an id; FirstOrDefault semantics must be preserved,
        // so the indexed lookup has to return the earliest grant in list order.
        var first = Grant("file.read");
        var second = Grant("file.read");
        var policy = DeterministicPolicy(Grant("log.write"), first, second);

        Assert.Same(first, policy.GetGrant("file.read"));
        Assert.True(policy.TryGetGrant("file.read", out var resolved));
        Assert.Same(first, resolved);
    }

    [Fact]
    public void GetGrant_skips_expired_grant_and_returns_next_active_grant()
    {
        // First grant is already expired at the logical clock; the lookup must fall
        // through to the next active grant with the same id rather than stopping.
        var expired = Grant("file.read", Now.AddMinutes(-1));
        var active = Grant("file.read", Now.AddMinutes(5));
        var policy = DeterministicPolicy(expired, active);

        Assert.True(policy.GrantsCapability("file.read"));
        Assert.Same(active, policy.GetGrant("file.read"));
    }

    [Fact]
    public void Expiry_is_evaluated_against_grant_clock_not_precomputed()
    {
        // The same grant must be active before its expiry and inactive after, proving
        // the index does not freeze active-state at construction time.
        var beforeExpiry = DeterministicPolicy(Grant("time.now", Now.AddMinutes(1)));
        var afterExpiry = beforeExpiry with { LogicalNow = Now.AddMinutes(2) };

        Assert.True(beforeExpiry.GrantsCapability("time.now"));
        Assert.False(afterExpiry.GrantsCapability("time.now"));
    }

    [Fact]
    public void Missing_capability_fails_closed()
    {
        var policy = DeterministicPolicy(Grant("log.write"));

        Assert.False(policy.GrantsCapability("net.http.get"));
        Assert.False(policy.TryGetGrant("net.http.get", out _));
        var error = Assert.Throws<SandboxRuntimeException>(() => policy.GetGrant("net.http.get"));
        Assert.Equal(SandboxErrorCode.PermissionDenied, error.Error.Code);
    }

    [Fact]
    public void With_copy_rebuilds_capability_index()
    {
        // A `with { Grants = ... }` copy must not reuse the source policy's index, so
        // capabilities removed or added in the copy resolve against the copy's grants.
        var original = DeterministicPolicy(Grant("log.write"));
        Assert.True(original.GrantsCapability("log.write"));

        var updated = original with { Grants = new[] { Grant("time.now") } };

        Assert.True(updated.GrantsCapability("time.now"));
        Assert.False(updated.GrantsCapability("log.write"));
        // Original policy index is untouched by the copy.
        Assert.True(original.GrantsCapability("log.write"));
        Assert.False(original.GrantsCapability("time.now"));
    }

    [Fact]
    public void Index_is_stable_across_repeated_lookups()
    {
        // Repeated calls must return consistent results from the cached index.
        var grant = Grant("file.write");
        var policy = DeterministicPolicy(Grant("log.write"), grant);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(policy.GrantsCapability("file.write"));
            Assert.Same(grant, policy.GetGrant("file.write"));
            Assert.False(policy.GrantsCapability("net.http.get"));
        }
    }
}
