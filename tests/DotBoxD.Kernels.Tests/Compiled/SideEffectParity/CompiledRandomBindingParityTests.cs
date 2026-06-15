using DotBoxD.Hosting;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Differential / parity tests for <c>random.nextI32</c> on the compiled side-effecting binding path.
///
/// PR #27 lifts the effects gate in BindingCallEmitter so that bindings declared with
/// <c>CompiledRuntime.CallBinding</c> stubs compile even when they carry a capability requirement,
/// an external effect, or a mandatory audit obligation. These tests run the same module
/// interpreted AND compiled with <c>AllowFallbackToInterpreter = false</c> and assert that every
/// observable — <c>Succeeded</c>, <c>ActualMode</c>, the returned I32 value, BindingCall audit
/// event fields (Kind, BindingId, CapabilityId, Effect, ResourceId), and
/// <c>ResourceUsage.HostCalls</c> — is identical between the two execution paths.
///
/// <c>ActualMode == Compiled</c> in the compiled run confirms the compiled path ran and did not
/// silently fall back to the interpreter.
///
/// Determinism: the policy is built with <c>.Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 42)</c>
/// so that both runs draw the same pseudo-random value from the same seed, enabling value equality
/// assertions between the two modes. The audit Timestamp is set to <c>DateTimeOffset.UnixEpoch</c>
/// by the deterministic clock, making it stable and comparable.
/// </summary>
public sealed class CompiledRandomBindingParityTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers: module JSON and host factory
    // ─────────────────────────────────────────────────────────────────────────

    private static SandboxHost RandomParity_CreateHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddRandomBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static string RandomParity_SingleCallModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "random" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "random.nextI32",
                    "args": [{ "i32": 0 }, { "i32": 100 }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string RandomParity_TwoCallModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "random" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "first",
                  "value": {
                    "call": "random.nextI32",
                    "args": [{ "i32": 0 }, { "i32": 500 }]
                  }
                },
                {
                  "op": "set",
                  "name": "second",
                  "value": {
                    "call": "random.nextI32",
                    "args": [{ "i32": 0 }, { "i32": 500 }]
                  }
                },
                {
                  "op": "return",
                  "value": { "op": "add", "left": { "var": "first" }, "right": { "var": "second" } }
                }
              ]
            }
          ]
        }
        """;

    private static SandboxPolicy RandomParity_DeterministicPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantRandom()
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 42UL)
            .WithFuel(10_000)
            .Build();

    private static async Task<SandboxExecutionResult> RandomParity_ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        ExecutionMode mode)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = mode,
                AllowFallbackToInterpreter = false,
            });

    // ─────────────────────────────────────────────────────────────────────────
    // (1) Compiled mode runs and returns a value in the requested range
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_compiled_succeeds_and_value_is_within_requested_range()
    {
        // Arrange
        var host = RandomParity_CreateHost();
        var module = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-range"));
        var plan = await host.PrepareAsync(module, RandomParity_DeterministicPolicy());

        // Act
        var result = await RandomParity_ExecuteAsync(host, plan, ExecutionMode.Compiled);

        // Assert
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        var value = ((I32Value)result.Value!).Value;
        Assert.InRange(value, 0, 99); // nextI32(0, 100) → [0, 100)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) Interpreted vs Compiled: ActualMode is correct for each
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_interpreted_run_has_ActualMode_Interpreted()
    {
        // Arrange
        var host = RandomParity_CreateHost();
        var module = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-mode-interp"));
        var plan = await host.PrepareAsync(module, RandomParity_DeterministicPolicy());

        // Act
        var result = await RandomParity_ExecuteAsync(host, plan, ExecutionMode.Interpreted);

        // Assert
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
    }

    [Fact]
    public async Task RandomNextI32_compiled_run_has_ActualMode_Compiled()
    {
        // Arrange
        var host = RandomParity_CreateHost();
        var module = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-mode-comp"));
        var plan = await host.PrepareAsync(module, RandomParity_DeterministicPolicy());

        // Act
        var result = await RandomParity_ExecuteAsync(host, plan, ExecutionMode.Compiled);

        // Assert
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (3) Differential: deterministic seed → same I32 value across both modes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_deterministic_same_seed_produces_same_value_in_both_modes()
    {
        // With the same deterministic policy seed, both modes must produce the identical
        // pseudo-random value.  This is the key differential assertion for random.nextI32.
        var host = RandomParity_CreateHost();
        var moduleInterp = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-value-interp"));
        var moduleComp = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-value-comp"));

        var policy = RandomParity_DeterministicPolicy();
        var planInterp = await host.PrepareAsync(moduleInterp, policy);
        var planComp = await host.PrepareAsync(moduleComp, policy);

        // Act
        var interp = await RandomParity_ExecuteAsync(host, planInterp, ExecutionMode.Interpreted);
        var comp = await RandomParity_ExecuteAsync(host, planComp, ExecutionMode.Compiled);

        // Assert
        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interp.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Both drew from the same seed → values must be equal.
        var interpValue = ((I32Value)interp.Value!).Value;
        var compValue = ((I32Value)comp.Value!).Value;
        Assert.Equal(interpValue, compValue);

        // The value must still respect the requested range [0, 100).
        Assert.InRange(compValue, 0, 99);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (4) Differential: audit event fields match field-for-field across modes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_compiled_BindingCall_audit_fields_match_interpreted_field_for_field()
    {
        // Runs the same module in both modes and compares every stable audit-event field:
        // Kind, BindingId, CapabilityId, Effect, ResourceId, Success.
        // Timestamp is set to DateTimeOffset.UnixEpoch by the deterministic clock,
        // so it too is comparable across runs.
        var host = RandomParity_CreateHost();
        var moduleInterp = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-audit-interp"));
        var moduleComp = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-audit-comp"));

        var policy = RandomParity_DeterministicPolicy();
        var planInterp = await host.PrepareAsync(moduleInterp, policy);
        var planComp = await host.PrepareAsync(moduleComp, policy);

        var interp = await RandomParity_ExecuteAsync(host, planInterp, ExecutionMode.Interpreted);
        var comp = await RandomParity_ExecuteAsync(host, planComp, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Locate the BindingCall audit event for random.nextI32 in both results.
        var ia = Assert.Single(
            interp.AuditEvents,
            e => e.Kind == "BindingCall" && e.BindingId == "random.nextI32");
        var ca = Assert.Single(
            comp.AuditEvents,
            e => e.Kind == "BindingCall" && e.BindingId == "random.nextI32");

        // Stable structural fields must be identical between modes.
        Assert.Equal(ia.Kind, ca.Kind);
        Assert.Equal(ia.BindingId, ca.BindingId);
        Assert.Equal(ia.CapabilityId, ca.CapabilityId);
        Assert.Equal(ia.Effect, ca.Effect);
        Assert.Equal(ia.ResourceId, ca.ResourceId);
        Assert.Equal(ia.Success, ca.Success);
        Assert.True(ia.Success);

        // Deterministic clock → Timestamp is DateTimeOffset.UnixEpoch in both modes.
        Assert.Equal(DateTimeOffset.UnixEpoch, ia.Timestamp);
        Assert.Equal(ia.Timestamp, ca.Timestamp);

        // Capability and effect must match the descriptor constants.
        Assert.Equal("random", ca.CapabilityId);
        Assert.Equal(SandboxEffect.Random, ca.Effect);
        Assert.Equal("random:i32", ca.ResourceId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (5) Differential: ResourceUsage.HostCalls matches across modes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_compiled_ResourceUsage_HostCalls_matches_interpreted()
    {
        var host = RandomParity_CreateHost();
        var moduleInterp = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-hcalls-interp"));
        var moduleComp = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-hcalls-comp"));

        var policy = RandomParity_DeterministicPolicy();
        var planInterp = await host.PrepareAsync(moduleInterp, policy);
        var planComp = await host.PrepareAsync(moduleComp, policy);

        var interp = await RandomParity_ExecuteAsync(host, planInterp, ExecutionMode.Interpreted);
        var comp = await RandomParity_ExecuteAsync(host, planComp, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Both modes must charge exactly one host call for one random.nextI32 invocation.
        Assert.Equal(1, interp.ResourceUsage.HostCalls);
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (6) Differential: two calls in the same module both emit audit events
    //     and resource accounting matches between modes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_two_calls_compiled_each_emit_audit_event_matching_interpreted()
    {
        var host = RandomParity_CreateHost();
        var moduleInterp = await host.ImportJsonAsync(RandomParity_TwoCallModuleJson("rand-parity-twocall-interp"));
        var moduleComp = await host.ImportJsonAsync(RandomParity_TwoCallModuleJson("rand-parity-twocall-comp"));

        var policy = RandomParity_DeterministicPolicy();
        var planInterp = await host.PrepareAsync(moduleInterp, policy);
        var planComp = await host.PrepareAsync(moduleComp, policy);

        var interp = await RandomParity_ExecuteAsync(host, planInterp, ExecutionMode.Interpreted);
        var comp = await RandomParity_ExecuteAsync(host, planComp, ExecutionMode.Compiled);

        Assert.True(interp.Succeeded, interp.Error?.SafeMessage);
        Assert.True(comp.Succeeded, comp.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, comp.ActualMode);

        // Exactly two BindingCall events for random.nextI32 in each mode.
        var iAudits = interp.AuditEvents.Where(e => e.Kind == "BindingCall" && e.BindingId == "random.nextI32").ToList();
        var cAudits = comp.AuditEvents.Where(e => e.Kind == "BindingCall" && e.BindingId == "random.nextI32").ToList();
        Assert.Equal(2, iAudits.Count);
        Assert.Equal(2, cAudits.Count);

        // Field-for-field parity for each call.
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(iAudits[i].BindingId, cAudits[i].BindingId);
            Assert.Equal(iAudits[i].CapabilityId, cAudits[i].CapabilityId);
            Assert.Equal(iAudits[i].Effect, cAudits[i].Effect);
            Assert.Equal(iAudits[i].ResourceId, cAudits[i].ResourceId);
            Assert.Equal(iAudits[i].Success, cAudits[i].Success);
        }

        // ResourceUsage: both modes charge two host calls.
        Assert.Equal(2, interp.ResourceUsage.HostCalls);
        Assert.Equal(interp.ResourceUsage.HostCalls, comp.ResourceUsage.HostCalls);

        // Deterministic seed → sum is reproducible and equal between modes.
        var interpSum = ((I32Value)interp.Value!).Value;
        var compSum = ((I32Value)comp.Value!).Value;
        Assert.Equal(interpSum, compSum);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (7) Capability enforcement: preparing without GrantRandom() throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_prepare_without_capability_grant_throws_validation_exception()
    {
        // Both modes share the same prepare step; this ensures capability enforcement
        // is not bypassed by the compiled path.
        var host = RandomParity_CreateHost();
        var module = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-no-cap"));

        // No GrantRandom() in the policy — must fail at prepare time.
        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().WithFuel(10_000).Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (8) Non-deterministic path: value is within range (no seed comparison)
    //     This test does not compare values between modes — it only asserts
    //     range, Succeeded, ActualMode, and that an audit event was emitted.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RandomNextI32_compiled_non_deterministic_value_is_within_range_and_audit_emitted()
    {
        // Uses a non-deterministic policy (no .Deterministic(...)) so the value cannot
        // be compared across runs. Asserts only the stable observables: success, mode,
        // range, and that a BindingCall audit event is present.
        var host = RandomParity_CreateHost();
        var module = await host.ImportJsonAsync(RandomParity_SingleCallModuleJson("rand-parity-nondeterministic"));
        var plan = await host.PrepareAsync(module,
            SandboxPolicyBuilder.Create()
                .GrantRandom()
                .WithFuel(10_000)
                .Build());

        var result = await RandomParity_ExecuteAsync(host, plan, ExecutionMode.Compiled);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);

        var value = ((I32Value)result.Value!).Value;
        Assert.InRange(value, 0, 99); // nextI32(0, 100) → [0, 100)

        var audit = Assert.Single(
            result.AuditEvents,
            e => e.Kind == "BindingCall" && e.BindingId == "random.nextI32");
        Assert.True(audit.Success);
        Assert.Equal("random", audit.CapabilityId);
        Assert.Equal(SandboxEffect.Random, audit.Effect);
        Assert.Equal("random:i32", audit.ResourceId);
    }
}
