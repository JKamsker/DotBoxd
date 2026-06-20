using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime;

public sealed class CapabilityGrantLookupCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Granted_capability_succeeds_across_require_and_get()
    {
        var grant = Grant("test.alpha", "alpha");
        var context = Context(grant);

        context.RequireCapability("test.alpha");
        var resolved = context.GetCapability("test.alpha");

        Assert.Same(grant, resolved);
        Assert.Equal("alpha", resolved.Parameters["marker"]);
    }

    [Fact]
    public void Denied_capability_still_throws_permission_denied_and_audits()
    {
        var audit = new InMemoryAuditSink();
        var context = Context(audit);

        var error = Assert.Throws<SandboxRuntimeException>(() => context.GetCapability("test.missing"));

        Assert.Equal(SandboxErrorCode.PermissionDenied, error.Error.Code);
        Assert.Equal("capability test.missing is not granted", error.Error.SafeMessage);
        var denial = Assert.Single(audit.Events);
        Assert.Equal("PolicyDenied", denial.Kind);
        Assert.Equal("test.missing", denial.CapabilityId);
        Assert.Equal("capability:test.missing", denial.ResourceId);
    }

    [Fact]
    public void Expired_grant_is_not_reused_when_effective_grant_clock_changes()
    {
        var grant = Grant("test.expiring", "expiring", Now.AddMinutes(1));
        var context = Context(grant);

        Assert.Same(grant, context.GetCapability("test.expiring"));

        using (context.BeginBindingGrantClockScope(Now.AddMinutes(2)))
        {
            var error = Assert.Throws<SandboxRuntimeException>(() => context.GetCapability("test.expiring"));
            Assert.Equal(SandboxErrorCode.PermissionDenied, error.Error.Code);
        }

        Assert.Same(grant, context.GetCapability("test.expiring"));
    }

    [Fact]
    public void Different_capability_id_does_not_reuse_cached_grant()
    {
        var alpha = Grant("test.alpha", "alpha");
        var beta = Grant("test.beta", "beta");
        var context = Context(alpha, beta);

        Assert.Same(alpha, context.GetCapability("test.alpha"));
        context.RequireCapability("test.beta");
        var resolved = context.GetCapability("test.beta");

        Assert.Same(beta, resolved);
        Assert.Equal("beta", resolved.Parameters["marker"]);
    }

    private static SandboxContext Context(params CapabilityGrant[] grants)
        => Context(new InMemoryAuditSink(), grants);

    private static SandboxContext Context(InMemoryAuditSink audit, params CapabilityGrant[] grants)
    {
        var policy = new SandboxPolicy(
            "grant-cache-test",
            SandboxEffect.Audit,
            grants,
            new ResourceLimits(MaxFuel: 1_000),
            Deterministic: true,
            LogicalNow: Now,
            RandomSeed: 1);

        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistryBuilder().Build(),
            audit,
            CancellationToken.None);
    }

    private static CapabilityGrant Grant(string id, string marker, DateTimeOffset? expiresAt = null)
        => new(
            id,
            new Dictionary<string, string> { ["marker"] = marker },
            expiresAt);
}
