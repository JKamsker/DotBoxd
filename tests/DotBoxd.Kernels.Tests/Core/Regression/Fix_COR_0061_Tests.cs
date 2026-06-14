namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for COR-0061: capability denial audits bypass the
/// deterministic audit clock. <see cref="SandboxContext.RequireCapability"/>
/// emits a <c>PolicyDenied</c> audit event when policy denies a capability,
/// but it stamps that event with <see cref="System.DateTimeOffset.UtcNow"/>
/// instead of routing through the context's deterministic audit clock
/// (<see cref="SandboxContext.AuditTimestamp"/>). Under a deterministic policy
/// with a logical clock, every emitted audit timestamp must be the logical
/// clock value so replays and deterministic audit comparisons stay stable.
/// </summary>
public sealed class Fix_COR_0061_Tests
{
    // A logical clock deliberately far from any plausible wall-clock value so a
    // wall-clock-stamped denial event is unambiguously distinguishable from the
    // deterministic timestamp the fix should produce.
    private static readonly DateTimeOffset LogicalNow =
        DateTimeOffset.Parse("2021-01-01T00:00:00Z");

    private const string UngrantedCapability = "fs.read";

    private static SandboxContext DeterministicContextWithoutGrant(InMemoryAuditSink audit)
    {
        // Deterministic policy carrying a logical clock but granting no
        // capabilities, so RequireCapability denies the requested capability.
        var policy = new SandboxPolicy(
            "deterministic-denial",
            SandboxEffects.Pure,
            [],
            new ResourceLimits(),
            Deterministic: true,
            LogicalNow: LogicalNow,
            RandomSeed: 1);

        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            audit,
            CancellationToken.None);
    }

    [Fact]
    public void RequireCapability_denial_audit_uses_policy_logical_clock()
    {
        var audit = new InMemoryAuditSink();
        var context = DeterministicContextWithoutGrant(audit);

        Assert.Throws<SandboxRuntimeException>(
            () => context.RequireCapability(UngrantedCapability));

        var denial = Assert.Single(
            audit.Events,
            e => e.Kind == "PolicyDenied" &&
                 e.CapabilityId == UngrantedCapability &&
                 !e.Success);

        // The context's deterministic audit clock would stamp this event with
        // the logical clock value (AuditTimestamp() == LogicalNow under a
        // deterministic policy with a logical clock).
        Assert.Equal(context.AuditTimestamp(), denial.Timestamp);

        // RED until COR-0061 is fixed: the denial is currently stamped with
        // DateTimeOffset.UtcNow, so the timestamp is the wall clock instead of
        // the deterministic logical clock.
        Assert.Equal(LogicalNow, denial.Timestamp);
    }
}
