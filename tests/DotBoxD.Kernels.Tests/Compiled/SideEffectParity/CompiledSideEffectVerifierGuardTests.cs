using DotBoxD.Hosting;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Verifier-negative dimension: asserts that the system's gates prevent side-effecting bindings
/// from bypassing policy or taking paths that are reserved for pure bindings.
/// These tests prove the guards hold — they do NOT expect the tests to silently succeed.
/// </summary>
public sealed class CompiledSideEffectVerifierGuardTests
{
    // -------------------------------------------------------------------------
    // Guard 1: A module that calls a side-effecting binding without the matching
    // capability grant must be REJECTED at PrepareAsync, before any execution.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Prepare_rejects_module_calling_side_effecting_binding_without_capability_grant()
    {
        var sink = new InMemoryPluginMessageSink();
        var host = VerifierGuard_CreatePluginHost(sink);
        var module = await host.ImportJsonAsync(VerifierGuard_MessageSendJson("guard-no-grant"));

        // Policy grants NO capability for host.message.write
        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(
                module,
                SandboxPolicyBuilder.Create().WithFuel(10_000).Build()));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
    }

    // -------------------------------------------------------------------------
    // (Former Guard 2 deleted.) Under PR #27 the compiler no longer rejects
    // side-effecting modules — compiling side effects is the whole point. The
    // "still rejected" invariant (a module needing a capability the policy does
    // not grant is refused at PrepareAsync with E-POLICY-CAP/E-POLICY-EFFECT) is
    // already asserted by
    // Prepare_rejects_module_calling_side_effecting_binding_without_capability_grant
    // above, so no replacement is added here.
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // Guard 3: Auto mode falls back to Interpreted when the module has
    // side-effecting bindings — it MUST NOT silently attempt compiled execution.
    // The sink delivery in interpreted mode confirms the binding actually ran.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Auto_mode_for_side_effecting_module_uses_interpreter_and_delivers_message()
    {
        var sink = new InMemoryPluginMessageSink();
        var host = VerifierGuard_CreatePluginHost(sink);
        var module = await host.ImportJsonAsync(VerifierGuard_MessageSendJson("guard-auto-mode"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantHostMessageWrite()
                .WithFuel(10_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        // Must have fallen back — the module carries side effects.
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        // Binding must have fired exactly once.
        var msg = Assert.Single(sink.Messages);
        Assert.Equal("player-1", msg.TargetId);
    }

    // -------------------------------------------------------------------------
    // Guard 4: BindingRegistryValidator must reject a capability-gated binding
    // that points to a DIRECT runtime method (i.e. not "CallBinding").
    // Only "CallBinding" is the approved generic stub for capability-gated paths;
    // direct methods are reserved for pure intrinsics.
    // Rule in BindingRegistryValidator: "uses a direct compiled runtime method
    // but is not a pure intrinsic" → E-BINDING-COMPILED.
    // -------------------------------------------------------------------------
    [Fact]
    public void Registry_rejects_capability_gated_binding_that_uses_direct_runtime_method()
    {
        // "math.abs" is an approved direct method, but pairing it with a
        // RequiredCapability and SideEffectingExternal must be rejected.
        var ex = Assert.Throws<SandboxValidationException>(() => SandboxHost.Create(builder =>
        {
            builder.AddBinding(VerifierGuard_CapabilityGatedDirectMethodBinding());
            builder.UseInterpreter();
        }));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    // -------------------------------------------------------------------------
    // Guard 5: BindingRegistryValidator must reject a binding that declares only
    // Cpu effects (no side effects) but still requires a capability.
    // Rule: "requires a capability but declares only pure CPU effects" → E-BINDING-EFFECT.
    // This ensures a binding cannot claim a capability "for free" without
    // declaring the matching effect in its signature.
    // -------------------------------------------------------------------------
    [Fact]
    public void Registry_rejects_capability_gated_binding_with_only_cpu_effect()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SandboxHost.Create(builder =>
        {
            builder.AddBinding(VerifierGuard_CapabilityGatedCpuOnlyBinding());
            builder.UseInterpreter();
        }));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-EFFECT");
    }

    // -------------------------------------------------------------------------
    // Guard 6: A revoked capability must block a side-effecting binding even when
    // the execution mode is Compiled. The revocation check happens before the
    // compiled artifact executes, so the sink must stay empty.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Revoked_capability_prevents_side_effecting_binding_in_compiled_mode_context()
    {
        var sink = new InMemoryPluginMessageSink();
        var host = VerifierGuard_CreatePluginHost(sink);
        var module = await host.ImportJsonAsync(VerifierGuard_MessageSendJson("guard-revocation"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantHostMessageWrite()
                .WithFuel(10_000)
                .Build());

        // Revoke before any execution.
        host.RevokeCapability(PluginMessageBindings.CapabilityId, "test-revocation-guard");

        // Even with Compiled mode requested, the revocation check happens first;
        // the system will either refuse at the policy gate (returning PolicyDenied)
        // or fall back to interpreted and then deny at runtime. Either way the
        // binding must NOT deliver a message.
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Empty(sink.Messages);
        // The audit trail must include a CapabilityRevoked event.
        Assert.Contains(result.AuditEvents, e => e.Kind == "CapabilityRevoked");
    }

    // -------------------------------------------------------------------------
    // Guard 7: Interpreted parity baseline — log.info (SideEffectingExternal,
    // capability "log.write") must produce exactly one SandboxLog audit event.
    // This proves that a side-effecting binding, when run interpreted, still
    // passes through the full audit enforcement path.
    // -------------------------------------------------------------------------
    [Fact]
    public async Task Interpreted_side_effecting_log_binding_produces_exactly_one_audit_event()
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(VerifierGuard_LogInfoJson("guard-log-audit"));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantLogging()
                .WithFuel(10_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        // Exactly one SandboxLog audit event for the log.info call.
        var logEvent = Assert.Single(result.AuditEvents, e => e.Kind == "SandboxLog");
        Assert.Equal("log.info", logEvent.BindingId);
        Assert.Equal("log.write", logEvent.CapabilityId);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // (Former Guard 8 deleted.) It asserted the compiler refuses host.message.send
    // with ValidationError — an invalid premise under PR #27, which executes that
    // binding compiled through the generic CallBinding dispatcher. Compiled
    // success + audit parity for host.message.send is covered by
    // CompiledSideEffectAuditParityTests; the "still rejected without a grant"
    // invariant is covered by
    // Prepare_rejects_module_calling_side_effecting_binding_without_capability_grant.
    // -------------------------------------------------------------------------

    // =========================================================================
    // Private helpers (prefixed VerifierGuard_ to avoid collisions when files
    // are combined in the same test assembly).
    // =========================================================================

    private static Hosting.SandboxHost VerifierGuard_CreatePluginHost(InMemoryPluginMessageSink sink)
        => Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static string VerifierGuard_MessageSendJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "host.message.send",
                    "args": [
                      { "string": "player-1" },
                      { "string": "hello-from-guard-test" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string VerifierGuard_LogInfoJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "log.info",
                    "args": [{ "string": "guard-log-entry" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// A binding that:
    /// - requires "host.message.write" capability (capability-gated)
    /// - has SideEffectingExternal safety
    /// - BUT points its compiled stub to a DIRECT runtime method ("AbsI32")
    ///   instead of the generic "CallBinding" stub.
    /// The BindingRegistryValidator must reject this with E-BINDING-COMPILED.
    /// </summary>
    private static BindingDescriptor VerifierGuard_CapabilityGatedDirectMethodBinding()
        => new(
            "verifier-guard.bad-direct",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            "host.message.write",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (_, args, _) => ValueTask.FromResult(args[0]),
            // Using a direct method ("AbsI32") instead of the generic "CallBinding".
            // This is the configuration that must be rejected.
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.AbsI32)),
            (grant, diagnostics) => { });

    /// <summary>
    /// A binding that:
    /// - requires "host.message.write" capability (capability-gated)
    /// - BUT declares ONLY Cpu effects (no side-effect flags).
    /// The BindingRegistryValidator must reject this with E-BINDING-EFFECT.
    /// </summary>
    private static BindingDescriptor VerifierGuard_CapabilityGatedCpuOnlyBinding()
        => new(
            "verifier-guard.cpu-only-cap",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffect.Cpu,  // Only CPU — no HostStateWrite or other side-effect
            "host.message.write",
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.FromInt32(0)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            (grant, diagnostics) => { });
}
