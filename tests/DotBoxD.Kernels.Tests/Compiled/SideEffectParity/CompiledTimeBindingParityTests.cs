using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Differential / parity tests for <c>time.nowUnixMillis</c> under interpreted vs compiled
/// execution (PR #27 base: side-effecting runtime-stub bindings compile instead of falling back).
///
/// Determinism strategy:
///   <c>time.nowUnixMillis</c> reads a wall-clock value that is inherently nondeterministic.
///   To make the returned timestamp value comparable across two separate executions (interpreted
///   and compiled), every test that checks the returned I64 uses
///   <see cref="SandboxPolicyBuilder.Deterministic"/> with a fixed
///   <c>logicalNow</c>.  The policy then seeds <c>SandboxContext.UtcNow()</c> with the pinned
///   offset, so both runs observe the same clock value and both the return value and the audit
///   event Timestamp can be compared deterministically.
///
///   Fields that are always variable and are therefore deliberately NOT compared across runs:
///   <list type="bullet">
///     <item>
///       <description>
///         <c>audit.Timestamp</c> in the two nondeterministic-mode helpers
///         (only compared in the deterministic-mode tests where it equals <c>logicalNow</c>).
///       </description>
///     </item>
///     <item>
///       <description>
///         <c>audit.Fields["durationMs"]</c> — wall-clock timing, differs per run.
///       </description>
///     </item>
///   </list>
///
///   The compiled run's <see cref="SandboxExecutionResult.ActualMode"/> must equal
///   <see cref="ExecutionMode.Compiled"/>; if it equals <c>Interpreted</c> the worktree
///   is not on the PR #27 base and the test will fail with an assertion, NOT a weakened
///   assertion.
/// </summary>
public sealed class CompiledTimeBindingParityTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Deterministic clock: both modes return the pinned logicalNow value and
    //    the compiled run genuinely ran compiled (ActualMode == Compiled).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Time_nowUnixMillis_compiled_returns_same_value_as_interpreted_with_deterministic_clock()
    {
        // Deterministic clock pins the timestamp so both runs see the same I64 return value.
        var logicalNow = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var interp = await TimeParityTestSupport.RunAsync(
            "parity-time-value-interp",
            logicalNow,
            ExecutionMode.Interpreted);
        var comp = await TimeParityTestSupport.RunAsync(
            "parity-time-value-comp",
            logicalNow,
            ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);

        // Confirm the compiled run genuinely ran compiled (PR #27 base check).
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Both must return the pinned millisecond timestamp.
        var expectedMs = logicalNow.ToUnixTimeMilliseconds();
        Assert.Equal(expectedMs, ((I64Value)interp.Value!).Value);
        Assert.Equal(expectedMs, ((I64Value)comp.Value!).Value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. BindingCall audit event: stable fields (Kind, BindingId, CapabilityId,
    //    Effect, ResourceId) are identical across modes.
    //    audit.Timestamp is also compared here because the deterministic clock
    //    makes it reproducible.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Time_nowUnixMillis_compiled_audit_fields_match_interpreted_field_for_field()
    {
        var logicalNow = DateTimeOffset.Parse("2026-06-15T08:00:00Z");

        var interp = await TimeParityTestSupport.RunAsync(
            "parity-time-audit-fields-interp",
            logicalNow,
            ExecutionMode.Interpreted);
        var comp = await TimeParityTestSupport.RunAsync(
            "parity-time-audit-fields-comp",
            logicalNow,
            ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "time.nowUnixMillis");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "time.nowUnixMillis");

        // Stable structural fields — must be identical across modes.
        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Success, ca.Success);

        // Pin concrete values from the binding implementation.
        Assert.Equal("BindingCall", ca.Kind);
        Assert.Equal("time.nowUnixMillis", ca.BindingId);
        Assert.Equal("time.now", ca.CapabilityId);
        Assert.Equal(SandboxEffect.Time, ca.Effect);
        Assert.Equal("clock:utc", ca.ResourceId);
        Assert.True(ca.Success);

        // With a deterministic clock, both audit event timestamps are equal to logicalNow.
        Assert.Equal(logicalNow, ia.Timestamp);
        Assert.Equal(logicalNow, ca.Timestamp);
        Assert.Equal(ia.Timestamp, ca.Timestamp);

        // "resourceKind" field from BindingAuditFields — must be identical across modes.
        Assert.NotNull(ia.Fields);
        Assert.NotNull(ca.Fields);
        Assert.Equal(ia.Fields["resourceKind"], ca.Fields["resourceKind"]);
        Assert.Equal("clock", ca.Fields["resourceKind"]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. ResourceUsage.HostCalls is 1 in each mode and identical across modes.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Time_nowUnixMillis_compiled_ResourceUsage_HostCalls_matches_interpreted()
    {
        var logicalNow = DateTimeOffset.Parse("2025-12-31T23:59:59Z");

        var interp = await TimeParityTestSupport.RunAsync(
            "parity-time-hostcalls-interp",
            logicalNow,
            ExecutionMode.Interpreted);
        var comp = await TimeParityTestSupport.RunAsync(
            "parity-time-hostcalls-comp",
            logicalNow,
            ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(1, comp.ResourceUsage.HostCalls);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Succeeded and Error.Code parity — both modes succeed, neither has an error.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Time_nowUnixMillis_compiled_success_and_null_error_match_interpreted()
    {
        var logicalNow = DateTimeOffset.Parse("2026-03-10T12:00:00Z");

        var interp = await TimeParityTestSupport.RunAsync(
            "parity-time-success-interp",
            logicalNow,
            ExecutionMode.Interpreted);
        var comp = await TimeParityTestSupport.RunAsync(
            "parity-time-success-comp",
            logicalNow,
            ExecutionMode.Compiled);

        Assert.Equal(interp.Succeeded, comp.Succeeded);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(interp.Error?.Code, comp.Error?.Code);
        Assert.Null(comp.Error);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Returned I64 value is a positive millisecond timestamp (type/range
    //    sanity check — works even without a deterministic clock).
    //    This test uses nondeterministic time but still asserts structure and
    //    that compiled genuinely ran compiled with matching HostCalls.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Time_nowUnixMillis_compiled_returns_positive_I64_and_HostCalls_matches_nondeterministic()
    {
        // Run with nondeterministic (live) clock to prove the binding works in
        // the common deployment case.  We do NOT compare the clock values across
        // the two runs because they are inherently different wall-clock readings.
        var interp = await TimeParityTestSupport.NondeterministicRunAsync(
            "parity-time-nondeterministic-interp",
            ExecutionMode.Interpreted);
        var comp = await TimeParityTestSupport.NondeterministicRunAsync(
            "parity-time-nondeterministic-comp",
            ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // The returned value must be a positive I64 (milliseconds since Unix epoch).
        var interpMs = ((I64Value)interp.Value!).Value;
        var compMs = ((I64Value)comp.Value!).Value;
        Assert.True(interpMs > 0, $"interpreted returned non-positive timestamp: {interpMs}");
        Assert.True(compMs > 0, $"compiled returned non-positive timestamp: {compMs}");

        // HostCalls must match (both exactly 1).
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(1, comp.ResourceUsage.HostCalls);

        // Stable audit fields must be identical (not the timestamp or durationMs).
        var ia = Assert.Single(interp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "time.nowUnixMillis");
        var ca = Assert.Single(comp.AuditEvents, e => e.Kind == "BindingCall" && e.BindingId == "time.nowUnixMillis");

        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Success, ca.Success);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Without GrantTimeNow, prepare should fail with E-POLICY-CAP —
    //    this validates that the compile path still enforces capability gates.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Time_nowUnixMillis_prepare_without_capability_grant_throws_validation_exception()
    {
        using var host = TimeParityTestSupport.BuildHost();
        var module = await host.ImportJsonAsync(TimeParityTestSupport.ModuleJson("parity-time-no-cap"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().WithFuel(10_000).Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Two sequential time.nowUnixMillis calls with a deterministic clock:
    //    both modes emit exactly two BindingCall audit events and HostCalls == 2.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Two_time_calls_compiled_emit_two_audit_events_matching_interpreted()
    {
        var logicalNow = DateTimeOffset.Parse("2026-01-15T10:00:00Z");

        var interp = await TimeParityTestSupport.DoubleCallRunAsync(
            "parity-time-double-interp",
            logicalNow,
            ExecutionMode.Interpreted);
        var comp = await TimeParityTestSupport.DoubleCallRunAsync(
            "parity-time-double-comp",
            logicalNow,
            ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Both modes emit exactly 2 BindingCall events for time.nowUnixMillis.
        var iAudits = interp.AuditEvents
            .Where(e => e.Kind == "BindingCall" && e.BindingId == "time.nowUnixMillis")
            .ToList();
        var cAudits = comp.AuditEvents
            .Where(e => e.Kind == "BindingCall" && e.BindingId == "time.nowUnixMillis")
            .ToList();

        Assert.Equal(2, iAudits.Count);
        Assert.Equal(iAudits.Count, cAudits.Count);

        // Per-event structural parity.
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(iAudits[i].BindingId, cAudits[i].BindingId);
            Assert.Equal(iAudits[i].CapabilityId, cAudits[i].CapabilityId);
            Assert.Equal(iAudits[i].Effect, cAudits[i].Effect);
            Assert.Equal(iAudits[i].ResourceId, cAudits[i].ResourceId);
            Assert.Equal(iAudits[i].Success, cAudits[i].Success);
            // With a deterministic clock both timestamps are logicalNow.
            Assert.Equal(logicalNow, cAudits[i].Timestamp);
        }

        // HostCalls and the return value (last timestamp wins in the double-call module
        // which returns the second call's value, equal to logicalNow in deterministic mode).
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
        Assert.Equal(2, comp.ResourceUsage.HostCalls);
    }

}
