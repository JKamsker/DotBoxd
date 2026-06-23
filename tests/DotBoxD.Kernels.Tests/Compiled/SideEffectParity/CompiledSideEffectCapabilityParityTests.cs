using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using static DotBoxD.Kernels.Tests.Compiled.SideEffectParity.CapabilityParityTestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Capability enforcement parity tests: a compiled side effect must NEVER happen without the
/// capability the interpreter would require.
///
/// Covers:
/// (a) Preparing a module that needs a capability the policy does not grant fails identically
///     for both host.message.send (host.message.write) and app.counter (game.write).
/// (b) Revoking a granted capability before execute → PolicyDenied + no side effect in BOTH modes.
/// (c) Target-restricted grant (allowedTargets): a denied target → PermissionDenied + no sink
///     delivery in BOTH modes (host.message.send only, because the app.counter custom binding
///     has no target scoping).
/// </summary>
public sealed class CompiledSideEffectCapabilityParityTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // (a) Prepare-time enforcement: missing capability grant → SandboxValidationException
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HostMessageSend_prepare_without_capability_grant_throws_validation_exception()
    {
        var sink = new Capability_InMemoryPluginMessageSink();
        using var host = CapabilityParity_MessageHost(sink);
        var module = await host.ImportJsonAsync(CapabilityParity_MessageModuleJson("msg-no-cap-1"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().WithFuel(10_000).Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task CustomCounterBinding_prepare_without_capability_grant_throws_validation_exception()
    {
        var counter = new Capability_Counter();
        using var host = CapabilityParity_CounterHost(counter);
        var module = await host.ImportJsonAsync(CapabilityParity_CounterModuleJson("counter-no-cap-1"));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().WithFuel(1_000).Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
        Assert.Equal(0, counter.TotalIncrement);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (b) Runtime revocation: revoking capability before execute → PolicyDenied + no side effect
    //     in BOTH interpreted AND compiled modes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HostMessageSend_revocation_before_execute_interpreted_yields_PolicyDenied_and_no_sink_delivery()
    {
        var sink = new Capability_InMemoryPluginMessageSink();
        using var host = CapabilityParity_MessageHost(sink);
        var module = await host.ImportJsonAsync(CapabilityParity_MessageModuleJson("msg-revoke-interp"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        host.RevokeCapability(PluginMessageBindings.CapabilityId, "test revocation");

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Empty(sink.Messages);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CapabilityRevoked");
    }

    [Fact]
    public async Task HostMessageSend_revocation_before_execute_compiled_yields_PolicyDenied_and_no_sink_delivery()
    {
        var sink = new Capability_InMemoryPluginMessageSink();
        using var host = CapabilityParity_MessageHost(sink);
        var module = await host.ImportJsonAsync(CapabilityParity_MessageModuleJson("msg-revoke-compiled"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        host.RevokeCapability(PluginMessageBindings.CapabilityId, "test revocation compiled");

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Empty(sink.Messages);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CapabilityRevoked");
    }

    [Fact]
    public async Task HostMessageSend_revocation_parity_interpreted_and_compiled_produce_same_error_code_and_no_side_effect()
    {
        // Differential test: revoke before execute and compare Succeeded + Error.Code between modes.
        var sink1 = new Capability_InMemoryPluginMessageSink();
        using var host1 = CapabilityParity_MessageHost(sink1);
        var module1 = await host1.ImportJsonAsync(CapabilityParity_MessageModuleJson("msg-revoke-parity-1"));
        var plan1 = await host1.PrepareAsync(module1, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());
        host1.RevokeCapability(PluginMessageBindings.CapabilityId, "parity test");
        var interpreted = await host1.ExecuteAsync(
            plan1,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        var sink2 = new Capability_InMemoryPluginMessageSink();
        using var host2 = CapabilityParity_MessageHost(sink2);
        var module2 = await host2.ImportJsonAsync(CapabilityParity_MessageModuleJson("msg-revoke-parity-2"));
        var plan2 = await host2.PrepareAsync(module2, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());
        host2.RevokeCapability(PluginMessageBindings.CapabilityId, "parity test");
        var compiled = await host2.ExecuteAsync(
            plan2,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Both must fail with identical error code.
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(interpreted.Error!.Code, compiled.Error!.Code);
        Assert.Equal(SandboxErrorCode.PolicyDenied, compiled.Error.Code);
        // No side effects in either case.
        Assert.Empty(sink1.Messages);
        Assert.Empty(sink2.Messages);
        // Both must have a CapabilityRevoked audit event.
        Assert.Contains(interpreted.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.Contains(compiled.AuditEvents, e => e.Kind == "CapabilityRevoked");
    }

    [Fact]
    public async Task CustomCounterBinding_revocation_before_execute_interpreted_yields_PolicyDenied_and_no_counter_increment()
    {
        var counter = new Capability_Counter();
        using var host = CapabilityParity_CounterHost(counter);
        var module = await host.ImportJsonAsync(CapabilityParity_CounterModuleJson("counter-revoke-interp"));
        var plan = await host.PrepareAsync(module, CapabilityParity_GameWritePolicy());

        host.RevokeCapability("game.write", "counter revocation test");

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Equal(0, counter.TotalIncrement);
        Assert.Contains(result.AuditEvents, e => e.Kind == "CapabilityRevoked");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (c) Target-restricted grant: denied target → PermissionDenied + no sink delivery
    //     in BOTH interpreted AND compiled modes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HostMessageSend_denied_target_interpreted_yields_PermissionDenied_and_no_sink_delivery()
    {
        var sink = new Capability_InMemoryPluginMessageSink();
        using var host = CapabilityParity_MessageHost(sink);
        // Module sends to "player-1" but policy only grants "player-2".
        var module = await host.ImportJsonAsync(CapabilityParity_MessageModuleJsonWithTarget("msg-denied-target-interp", "player-1"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-2"])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task HostMessageSend_denied_target_compiled_fails_and_produces_no_sink_delivery()
    {
        // PR #27: compiled mode executes host.message.send through the generic CallBinding
        // dispatcher, so the runtime target check fires exactly as it does interpreted. A denied
        // target yields PermissionDenied (NOT ValidationError) and delivers nothing to the sink.
        var sink = new Capability_InMemoryPluginMessageSink();
        using var host = CapabilityParity_MessageHost(sink);
        // Module sends to "player-1" but policy only grants "player-2".
        var module = await host.ImportJsonAsync(CapabilityParity_MessageModuleJsonWithTarget("msg-denied-target-compiled", "player-1"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-2"])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        // Same runtime target check as the interpreter → PermissionDenied.
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Empty(sink.Messages);
    }

    /// <summary>
    /// PARITY: When the granted target list excludes the requested target, BOTH the interpreter
    /// and the compiled path (PR #27 runs host.message.send through the generic CallBinding
    /// dispatcher) apply the same runtime target check and return the identical error code,
    /// <see cref="SandboxErrorCode.PermissionDenied"/>. Neither mode delivers a side effect.
    /// </summary>
    [Fact]
    public async Task HostMessageSend_denied_target_parity_same_error_code_and_no_side_effect_in_either_mode()
    {
        // Differential: denied target; compare Succeeded, Error.Code, and sink state across modes.
        var sink1 = new Capability_InMemoryPluginMessageSink();
        using var host1 = CapabilityParity_MessageHost(sink1);
        var module1 = await host1.ImportJsonAsync(CapabilityParity_MessageModuleJsonWithTarget("msg-target-parity-1", "player-1"));
        var plan1 = await host1.PrepareAsync(module1, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-2"])
            .WithFuel(10_000)
            .Build());
        var interpreted = await host1.ExecuteAsync(
            plan1,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        var sink2 = new Capability_InMemoryPluginMessageSink();
        using var host2 = CapabilityParity_MessageHost(sink2);
        var module2 = await host2.ImportJsonAsync(CapabilityParity_MessageModuleJsonWithTarget("msg-target-parity-2", "player-1"));
        var plan2 = await host2.PrepareAsync(module2, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-2"])
            .WithFuel(10_000)
            .Build());
        var compiled = await host2.ExecuteAsync(
            plan2,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Both must fail (no side effect).
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Empty(sink1.Messages);
        Assert.Empty(sink2.Messages);

        // The compiled run genuinely ran compiled (no fallback).
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        // True parity: identical error code in both modes (the runtime target check).
        Assert.Equal(SandboxErrorCode.PermissionDenied, interpreted.Error!.Code);
        Assert.Equal(SandboxErrorCode.PermissionDenied, compiled.Error!.Code);
        Assert.Equal(interpreted.Error.Code, compiled.Error.Code);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Allowed target → success in both modes (sanity check / positive coverage)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HostMessageSend_allowed_target_interpreted_succeeds_with_sink_delivery()
    {
        var sink = new Capability_InMemoryPluginMessageSink();
        using var host = CapabilityParity_MessageHost(sink);
        var module = await host.ImportJsonAsync(CapabilityParity_MessageModuleJsonWithTarget("msg-allowed-target-interp", "player-1"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-1"])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Single(sink.Messages);
        Assert.Equal("player-1", sink.Messages[0].TargetId);
    }

    [Fact]
    public async Task HostMessageSend_compiled_mode_with_side_effecting_binding_succeeds_without_fallback()
    {
        // PR #27: compiled mode now executes SideEffectingExternal bindings (host.message.send)
        // directly through the generic CallBinding dispatcher — no interpreter fallback required.
        // The compiled run must succeed (ActualMode==Compiled) and deliver to the sink.
        var sink = new Capability_InMemoryPluginMessageSink();
        using var host = CapabilityParity_MessageHost(sink);
        var module = await host.ImportJsonAsync(CapabilityParity_MessageModuleJson("msg-compiled-no-fallback"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        // Ran compiled — did NOT silently fall back to the interpreter.
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        // The side effect must have been delivered to the sink.
        var msg = Assert.Single(sink.Messages);
        Assert.Equal("player-1", msg.TargetId);
    }

}
