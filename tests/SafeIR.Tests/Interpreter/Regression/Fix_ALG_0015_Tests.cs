using SafeIR;
using SafeIR.Hosting;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for ALG-0015: a single policy validation pass must probe capability
/// membership against one captured grant clock instead of re-reading the wall clock (and
/// rescanning grants) for every requested/required capability. The fix adds the
/// <see cref="SandboxPolicy.GrantsCapability(string, DateTimeOffset)"/> overload so
/// <c>PolicyResolver</c> can capture <c>GrantClock</c> once and reuse it across the cached
/// grant index. These tests pin the observable contract that change must preserve exactly:
/// the clock-aware probe matches call-time expiry semantics, agrees with the live-clock
/// overload, and leaves prepare-time policy diagnostics unchanged.
/// </summary>
public sealed class Fix_ALG_0015_Tests
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
    public void Clock_aware_probe_evaluates_expiry_against_supplied_clock()
    {
        // The validation pass passes one captured clock; expiry must be judged against
        // that value, not a freshly read wall clock, so the same grant flips active state
        // purely by moving the supplied clock past its expiry.
        var policy = DeterministicPolicy(Grant("time.now", Now.AddMinutes(1)));

        Assert.True(policy.GrantsCapability("time.now", Now));
        Assert.True(policy.GrantsCapability("time.now", Now.AddSeconds(59)));
        Assert.False(policy.GrantsCapability("time.now", Now.AddMinutes(2)));
    }

    [Fact]
    public void Clock_aware_probe_agrees_with_live_clock_overload()
    {
        // For a deterministic policy the live GrantClock is LogicalNow, so passing that
        // same instant to the overload must yield identical membership answers. This
        // guarantees the new pass-time path does not diverge from the runtime path.
        var policy = DeterministicPolicy(
            Grant("log.write"),
            Grant("file.read", Now.AddMinutes(-1)),
            Grant("file.read", Now.AddMinutes(5)));

        var clock = policy.GrantClock;
        Assert.Equal(policy.GrantsCapability("log.write"), policy.GrantsCapability("log.write", clock));
        Assert.Equal(policy.GrantsCapability("file.read"), policy.GrantsCapability("file.read", clock));
        Assert.Equal(policy.GrantsCapability("net.http.get"), policy.GrantsCapability("net.http.get", clock));
        Assert.True(policy.GrantsCapability("file.read", clock));
    }

    [Fact]
    public void Clock_aware_probe_is_consistent_across_many_capabilities()
    {
        // A pass captures the clock once and probes many capabilities. Repeated probes
        // with the same clock must be stable and fail closed for capabilities that have no
        // active grant, mirroring the per-capability loops in PolicyResolver.
        var policy = DeterministicPolicy(Grant("log.write"), Grant("time.now"));
        var clock = policy.GrantClock;

        for (var i = 0; i < 16; i++)
        {
            Assert.True(policy.GrantsCapability("log.write", clock));
            Assert.True(policy.GrantsCapability("time.now", clock));
            Assert.False(policy.GrantsCapability("random", clock));
            Assert.False(policy.GrantsCapability("net.http.get", clock));
        }
    }

    [Fact]
    public async Task Prepare_still_grants_satisfied_capability_request()
    {
        // End-to-end through the production prepare path: a requested capability backed by
        // an active grant must validate cleanly after the single-clock loop change.
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(RequestJson("time.now"));
        var policy = new SandboxPolicy(
            "request-granted",
            SandboxEffects.Pure | SandboxEffect.Time,
            [new CapabilityGrant("time.now", new Dictionary<string, string>())],
            new ResourceLimits(MaxFuel: 5_000));

        var plan = await host.PrepareAsync(module, policy);

        Assert.NotNull(plan);
    }

    [Fact]
    public async Task Prepare_still_rejects_ungranted_capability_request()
    {
        // The requested-capability loop must keep emitting E-POLICY-CAP when no active
        // grant covers the request, proving the shared clock did not weaken denial.
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(RequestJson("time.now"));
        var policy = new SandboxPolicy(
            "request-denied",
            SandboxEffects.Pure | SandboxEffect.Time,
            [],
            new ResourceLimits(MaxFuel: 5_000));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    private static string RequestJson(string capabilityId)
        => $$"""
        {
          "id": "alg-0015-request",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "{{capabilityId}}" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """;
}
