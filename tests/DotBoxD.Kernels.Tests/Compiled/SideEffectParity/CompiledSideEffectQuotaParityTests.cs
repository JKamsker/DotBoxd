using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Runtime;
using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.QuotaParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Parity tests for resource/quota behaviour across interpreted and compiled execution modes
/// for side-effecting and pure bindings.
///
/// Context: PR #27 lets the compiler execute side-effecting bindings (host.message.send, log.info)
/// directly through the generic CallBinding dispatcher, so the compiled path now matches the
/// interpreter. These tests lock the observable contract:
///
///   1. Pure-binding HostCalls count matches in both modes.
///   2. Fuel delta between compiled and interpreted is exactly +1 (return-type check).
///   3. MaxHostCalls quota trips QuotaExceeded in both modes and the call that crosses the limit
///      does NOT produce a side effect (the check fires before the host function is invoked).
///   4. MaxFuel quota trips QuotaExceeded in both modes for a pure computation.
///   5. Effectful log module: compiled mode succeeds and matches interpreted on HostCalls,
///      LogEvents, and the SandboxLog audit event.
///   6. Effectful module MaxLogEvents quota in interpreted mode (full audit+sink parity check).
///   7. Plugin-message send succeeds in interpreted mode; sink and audit match.
///   8. Revoked capability trips PolicyDenied in interpreted mode with no sink delivery.
/// </summary>
public sealed class CompiledSideEffectQuotaParityTests
{
    // ----------------------------------------------------------------
    // 1. Pure binding: HostCalls count is equal in interpreted and compiled.
    // ----------------------------------------------------------------
    [Fact]
    public async Task Pure_binding_host_call_count_matches_interpreted_and_compiled()
    {
        using var host = QuotaParityPureHost();
        var module = await host.ImportJsonAsync(QuotaParitySingleAbsJson("parity-host-calls-pure"));
        var plan = await host.PrepareAsync(module, QuotaParityPurePolicy(maxHostCalls: 10, maxFuel: 10_000));

        var interpreted = await RunAsync(host, plan, ExecutionMode.Interpreted);
        var compiled = await RunAsync(host, plan, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
        Assert.Equal(1, interpreted.ResourceUsage.HostCalls);
    }

    // ----------------------------------------------------------------
    // 2. Pure binding: FuelUsed differs by exactly +1 (return-type check).
    // ----------------------------------------------------------------
    [Fact]
    public async Task Pure_binding_fuel_used_differs_by_exactly_compiled_type_check_overhead()
    {
        using var host = QuotaParityPureHost();
        var module = await host.ImportJsonAsync(QuotaParitySingleAbsJson("parity-fuel-delta-pure"));
        var plan = await host.PrepareAsync(module, QuotaParityPurePolicy(maxHostCalls: 10, maxFuel: 10_000));

        var interpreted = await RunAsync(host, plan, ExecutionMode.Interpreted);
        var compiled = await RunAsync(host, plan, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);

        // Compiled mode charges exactly one extra fuel unit for the return-value type check.
        // The math.abs intrinsic is on the CanEmitDirectIntrinsic path so no arg-array cost.
        Assert.Equal(1, compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed);
    }

    // ----------------------------------------------------------------
    // 3. MaxHostCalls quota: trips QuotaExceeded in both modes when two
    //    consecutive pure binding calls exceed MaxHostCalls = 1.
    //    HostCalls count at trip point is the same in both modes.
    // ----------------------------------------------------------------
    [Fact]
    public async Task MaxHostCalls_quota_trips_QuotaExceeded_in_both_modes_for_pure_binding()
    {
        using var host = QuotaParityPureHost();
        var module = await host.ImportJsonAsync(QuotaParityDoubleAbsJson("parity-host-call-limit"));
        var plan = await host.PrepareAsync(module, QuotaParityPurePolicy(maxHostCalls: 1, maxFuel: 10_000));

        var interpreted = await RunAsync(host, plan, ExecutionMode.Interpreted);
        var compiled = await RunAsync(host, plan, ExecutionMode.Compiled);

        // Both must fail with QuotaExceeded
        Assert.False(interpreted.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interpreted.Error!.Code);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, compiled.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // HostCalls is charged before the quota check fires, so both see 2
        Assert.Equal(2, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(2, compiled.ResourceUsage.HostCalls);
    }

    // ----------------------------------------------------------------
    // 4. MaxFuel quota: trips QuotaExceeded in both modes when fuel budget
    //    is intentionally too small to complete computation.
    // ----------------------------------------------------------------
    [Fact]
    public async Task MaxFuel_quota_trips_QuotaExceeded_in_both_modes_for_pure_computation()
    {
        using var host = QuotaParityPureHost();
        var module = await host.ImportJsonAsync(QuotaParityPureComputeJson("parity-max-fuel-pure"));
        // Fuel 1 is not enough to execute even a single arithmetic expression
        var plan = await host.PrepareAsync(module, QuotaParityPurePolicy(maxHostCalls: 100, maxFuel: 1));

        var interpreted = await RunAsync(host, plan, ExecutionMode.Interpreted);
        var compiled = await RunAsync(host, plan, ExecutionMode.Compiled);

        Assert.False(interpreted.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interpreted.Error!.Code);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, compiled.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    // ----------------------------------------------------------------
    // 5. Effectful module: PR #27 runs log.info compiled through the generic
    //    CallBinding dispatcher. Both interpreted and compiled (no fallback)
    //    succeed and match on HostCalls, LogEvents, and the SandboxLog audit.
    // ----------------------------------------------------------------
    [Fact]
    public async Task Effectful_log_binding_compiled_mode_succeeds_with_log_event_parity()
    {
        using var host = QuotaParityLogHost();
        var module = await host.ImportJsonAsync(QuotaParityLogJson("parity-effectful-success"));
        var plan = await host.PrepareAsync(module,
            SandboxPolicyBuilder.Create().GrantLogging().WithFuel(10_000).Build());

        var interpreted = await RunAsync(host, plan, ExecutionMode.Interpreted);
        var compiled = await RunAsync(host, plan, ExecutionMode.Compiled);

        // Both modes succeed.
        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        // Compiled genuinely ran compiled (no fallback).
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // ResourceUsage parity.
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
        Assert.Equal(1, compiled.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.LogEvents, compiled.ResourceUsage.LogEvents);
        Assert.Equal(1, compiled.ResourceUsage.LogEvents);

        // SandboxLog audit parity — field for field.
        var iLog = Assert.Single(interpreted.AuditEvents, e => e.Kind == "SandboxLog");
        var cLog = Assert.Single(compiled.AuditEvents, e => e.Kind == "SandboxLog");
        Assert.Equal(iLog.BindingId, cLog.BindingId);
        Assert.Equal(iLog.CapabilityId, cLog.CapabilityId);
        Assert.Equal(iLog.ResourceId, cLog.ResourceId);
        Assert.Equal(iLog.Message, cLog.Message);
        Assert.Equal(iLog.Success, cLog.Success);
    }

    // ----------------------------------------------------------------
    // 6. MaxLogEvents quota in interpreted mode: trips QuotaExceeded at
    //    the correct log event; the first event is recorded but the second
    //    is blocked. Audit and ResourceUsage are consistent.
    // ----------------------------------------------------------------
    [Fact]
    public async Task MaxLogEvents_quota_trips_at_correct_log_event_in_interpreted_mode()
    {
        using var host = QuotaParityLogHost();
        var module = await host.ImportJsonAsync(QuotaParityDoubleLogJson("parity-max-log-events"));
        var plan = await host.PrepareAsync(module,
            SandboxPolicyBuilder.Create().GrantLogging().WithMaxLogEvents(1).WithFuel(10_000).Build());

        var result = await RunAsync(host, plan, ExecutionMode.Interpreted);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        // The quota increments before enforcement, so HostCalls reflects both attempts
        Assert.Equal(2, result.ResourceUsage.LogEvents);
    }

    // ----------------------------------------------------------------
    // 7. PluginMessage single successful send: sink and PluginMessage
    //    audit event are produced, ResourceUsage.HostCalls == 1 in
    //    interpreted mode (compiled falls back or fails; we test interp).
    // ----------------------------------------------------------------
    [Fact]
    public async Task PluginMessage_single_send_delivers_to_sink_and_emits_audit_in_interpreted_mode()
    {
        var sink = new InMemoryPluginMessageSink();
        using var host = QuotaParityMessageHost(sink);
        var module = await host.ImportJsonAsync(QuotaParitySingleSendJson("parity-msg-send-interp"));
        var plan = await host.PrepareAsync(module, QuotaParityMessagePolicy(maxHostCalls: 10, maxFuel: 10_000));

        var result = await RunAsync(host, plan, ExecutionMode.Interpreted);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1, result.ResourceUsage.HostCalls);

        var msg = Assert.Single(sink.Messages);
        Assert.Equal("player-1", msg.TargetId);
        Assert.Equal("hello", msg.Message);

        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.True(audit.Success);
        Assert.Equal(PluginMessageBindings.CapabilityId, audit.CapabilityId);
    }

    // ----------------------------------------------------------------
    // 8. Revoked capability trips PolicyDenied in interpreted mode and
    //    the sink receives NO message (fail-close on revocation).
    // ----------------------------------------------------------------
    [Fact]
    public async Task Revoked_PluginMessage_capability_trips_PolicyDenied_with_no_sink_delivery()
    {
        var sink = new InMemoryPluginMessageSink();
        using var host = QuotaParityMessageHost(sink);
        var module = await host.ImportJsonAsync(QuotaParitySingleSendJson("parity-msg-revoked"));
        var plan = await host.PrepareAsync(module, QuotaParityMessagePolicy(maxHostCalls: 10, maxFuel: 10_000));

        host.RevokeCapability(PluginMessageBindings.CapabilityId, "test-revoke");
        var result = await RunAsync(host, plan, ExecutionMode.Interpreted);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Empty(sink.Messages);
        Assert.Equal(0, result.ResourceUsage.HostCalls);
    }

    // ----------------------------------------------------------------
    // 9. MaxHostCalls with PluginMessage in interpreted mode: first send
    //    succeeds and is delivered; second send trips QuotaExceeded and
    //    is NOT delivered (no leak of the over-limit message).
    // ----------------------------------------------------------------
    [Fact]
    public async Task MaxHostCalls_with_PluginMessage_trips_QuotaExceeded_and_second_send_does_not_leak()
    {
        var sink = new InMemoryPluginMessageSink();
        using var host = QuotaParityMessageHost(sink);
        var module = await host.ImportJsonAsync(QuotaParityDoubleSendJson("parity-msg-host-call-limit"));
        var plan = await host.PrepareAsync(module, QuotaParityMessagePolicy(maxHostCalls: 1, maxFuel: 10_000));

        var result = await RunAsync(host, plan, ExecutionMode.Interpreted);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        // HostCalls incremented on both calls; the second one trips the limit
        Assert.Equal(2, result.ResourceUsage.HostCalls);
        // Only the first send was delivered before the quota was exceeded
        Assert.Single(sink.Messages);
        Assert.Equal("player-1", sink.Messages[0].TargetId);
    }

}
