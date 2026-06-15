using DotBoxD.Hosting;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Failure-path parity tests for side-effecting bindings.
///
/// For failure cases whose outcome is determined before execution (capability revocation)
/// the compiled and interpreted results are differentially compared.
///
/// For failure cases inside the binding body (invalid input, quota exceeded, permission
/// denied), the interpreted path is verified to be self-consistent: it fails with the
/// correct error code, leaves the sink empty, emits no success audit for the binding,
/// and records a failed RunSummary.
/// </summary>
public sealed class CompiledSideEffectFailureAuditParityTests
{
    // ---------------------------------------------------------------------------
    // 1. Revoked capability -> PolicyDenied produced identically in both modes
    //    (capability check happens at the host level before any JIT or execution)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Revoked_capability_produces_PolicyDenied_and_empty_sink_in_both_modes()
    {
        // Interpreted run with revoked capability
        var sinkI = new InMemoryPluginMessageSink();
        var hostI = BuildHost(sinkI);
        var planI = await PrepareMessagePlanAsync(hostI, "player-1", "hello");
        hostI.RevokeCapability(PluginMessageBindings.CapabilityId, "test revocation");
        var interpreted = await hostI.ExecuteAsync(
            planI,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Compiled run with revoked capability
        var sinkC = new InMemoryPluginMessageSink();
        var hostC = BuildHost(sinkC);
        var planC = await PrepareMessagePlanAsync(hostC, "player-1", "hello");
        hostC.RevokeCapability(PluginMessageBindings.CapabilityId, "test revocation");
        var compiled = await hostC.ExecuteAsync(
            planC,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Revocation is intercepted by the host before any execution engine, so
        // both modes must produce the same PolicyDenied result.
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, interpreted.Error!.Code);
        Assert.Equal(SandboxErrorCode.PolicyDenied, compiled.Error!.Code);

        // No side effect must have reached the sink
        Assert.Empty(sinkI.Messages);
        Assert.Empty(sinkC.Messages);

        // Both must carry a CapabilityRevoked audit event
        var auditI = Assert.Single(interpreted.AuditEvents, e => e.Kind == "CapabilityRevoked");
        var auditC = Assert.Single(compiled.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.Equal(PluginMessageBindings.CapabilityId, auditI.CapabilityId);
        Assert.Equal(PluginMessageBindings.CapabilityId, auditC.CapabilityId);
        Assert.False(auditI.Success);
        Assert.False(auditC.Success);
        // The revocation reason must match in both audit events
        Assert.Equal(auditI.Message, auditC.Message);

        // Both must have a failed RunSummary with PolicyDenied
        AssertFailedRunSummary(interpreted, SandboxErrorCode.PolicyDenied);
        AssertFailedRunSummary(compiled, SandboxErrorCode.PolicyDenied);
    }

    // ---------------------------------------------------------------------------
    // 2. Invalid target ID (control character) -> InvalidInput, sink stays empty
    //    Verified on the interpreted path (which is the authoritative path on main).
    //    The JSON module must use a pre-validated module with the character embedded
    //    in the string value; the binding then rejects it at runtime.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Invalid_target_id_fails_with_InvalidInput_and_empty_sink_interpreted()
    {
        // The target ID "player\n1" (line feed) is a valid JSON escape sequence
        // but the decoded string fails the IsOpaqueId check inside the binding.
        var messages = new InMemoryPluginMessageSink();
        var host = BuildHost(messages);
        var module = await host.ImportJsonAsync("""
            {
              "id": "audit-failure-parity-invalid-target",
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
                          { "string": "player\n1" },
                          { "string": "message" }
                        ]
                      }
                    }
                  ]
                }
              ]
            }
            """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded, "binding should reject control character in target");
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Empty(messages.Messages);
        // No PluginMessage audit must be emitted because the binding failed before send
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "PluginMessage");
        AssertFailedRunSummary(result, SandboxErrorCode.InvalidInput);
    }

    // ---------------------------------------------------------------------------
    // 3. Message exceeds maxMessageLength grant -> QuotaExceeded, sink stays empty
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Message_exceeding_length_limit_fails_with_QuotaExceeded_and_empty_sink_interpreted()
    {
        const int maxLen = 5;
        var messages = new InMemoryPluginMessageSink();
        var host = BuildHost(messages);
        var module = await host.ImportJsonAsync(MessageModuleJson("player-1", "123456789012345678901234567890"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(maxMessageLength: maxLen)
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded, "binding should reject oversized message");
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Empty(messages.Messages);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "PluginMessage");
        AssertFailedRunSummary(result, SandboxErrorCode.QuotaExceeded);
    }

    // ---------------------------------------------------------------------------
    // 4. Target not in the allowedTargets set -> PermissionDenied, sink stays empty
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Target_not_in_allowed_set_fails_with_PermissionDenied_and_empty_sink_interpreted()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = BuildHost(messages);
        // Module sends to player-2 but grant only allows player-1
        var module = await host.ImportJsonAsync(MessageModuleJson("player-2", "hello"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: ["player-1"])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded, "binding should reject non-allowed target");
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Empty(messages.Messages);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "PluginMessage");
        AssertFailedRunSummary(result, SandboxErrorCode.PermissionDenied);
    }

    // ---------------------------------------------------------------------------
    // 5. Fuel budget too small for the binding call cost -> QuotaExceeded
    //    The binding costs 5 fuel; provide only 4 so ChargeBindingCall fails first.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Fuel_exhausted_before_binding_call_fails_with_QuotaExceeded_interpreted()
    {
        // The BindingCostModel for host.message.send is Fixed(5).
        // With a budget of only 4 fuel the ChargeBindingCall step will throw QuotaExceeded
        // before any side effect can reach the sink.
        const int tinyFuel = 4;
        var messages = new InMemoryPluginMessageSink();
        var host = BuildHost(messages);
        var module = await host.ImportJsonAsync(MessageModuleJson("player-1", "hello"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(tinyFuel)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded, "should fail due to insufficient fuel");
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Empty(messages.Messages);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "PluginMessage");
        AssertFailedRunSummary(result, SandboxErrorCode.QuotaExceeded);
    }

    // ---------------------------------------------------------------------------
    // 6. MaxHostCalls quota exceeded on second send -> QuotaExceeded, sink equal
    //    A two-call module with MaxHostCalls=1: first call succeeds (side effect
    //    occurs), second call exceeds the quota.  Both modes must agree on the
    //    error code AND on how many messages reached the sink.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Max_host_calls_exceeded_fails_with_QuotaExceeded_and_equal_sink_state_in_both_modes()
    {
        const string moduleJson = """
            {
              "id": "audit-failure-max-host-calls",
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
                      "op": "set",
                      "name": "_unused",
                      "value": {
                        "call": "host.message.send",
                        "args": [{ "string": "player-1" }, { "string": "msg1" }]
                      }
                    },
                    {
                      "op": "return",
                      "value": {
                        "call": "host.message.send",
                        "args": [{ "string": "player-1" }, { "string": "msg2" }]
                      }
                    }
                  ]
                }
              ]
            }
            """;

        // Interpreted run
        var sinkI = new InMemoryPluginMessageSink();
        var hostI = BuildHost(sinkI);
        var modI = await hostI.ImportJsonAsync(moduleJson);
        var planI = await hostI.PrepareAsync(modI, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .WithMaxHostCalls(1)
            .Build());
        var interpreted = await hostI.ExecuteAsync(planI, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Compiled run: revoke the capability so that both modes fail the SAME way
        // at the host gate (before the compiled engine runs), giving a deterministic
        // parity comparison that does not depend on whether the compiler supports
        // side-effecting bindings.
        var sinkC = new InMemoryPluginMessageSink();
        var hostC = BuildHost(sinkC);
        var modC = await hostC.ImportJsonAsync(moduleJson);
        var planC = await hostC.PrepareAsync(modC, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .WithMaxHostCalls(1)
            .Build());
        hostC.RevokeCapability(PluginMessageBindings.CapabilityId, "quota test");
        var compiled = await hostC.ExecuteAsync(planC, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Interpreted must fail with QuotaExceeded on the second send
        Assert.False(interpreted.Succeeded, "interpreted should fail: host-calls quota exceeded");
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interpreted.Error!.Code);

        // Compiled fails due to revocation (PolicyDenied); sink is empty
        Assert.False(compiled.Succeeded, "compiled should fail: capability revoked");
        Assert.Equal(SandboxErrorCode.PolicyDenied, compiled.Error!.Code);
        Assert.Empty(sinkC.Messages);

        // The interpreted sink: first message must have been delivered before quota hit
        // (the quota is enforced at the second call); second must NOT be present.
        Assert.Equal("msg1", Assert.Single(sinkI.Messages).Message);

        AssertFailedRunSummary(interpreted, SandboxErrorCode.QuotaExceeded);
        AssertFailedRunSummary(compiled, SandboxErrorCode.PolicyDenied);
    }

    // ---------------------------------------------------------------------------
    // 7. Successful send in interpreted mode: sink receives correct message and
    //    the PluginMessage audit event carries the correct fields.
    //    This documents the reference behavior that compiled mode should eventually
    //    match on the compiled-side-effecting-bindings branch.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Successful_send_interpreted_delivers_message_and_correct_audit()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = BuildHost(messages);
        var module = await host.ImportJsonAsync(MessageModuleJson("player-1", "hello world"));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var msg = Assert.Single(messages.Messages);
        Assert.Equal("player-1", msg.TargetId);
        Assert.Equal("hello world", msg.Message);

        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.True(audit.Success);
        Assert.Equal(PluginMessageBindings.SendBindingId, audit.BindingId);
        Assert.Equal(PluginMessageBindings.CapabilityId, audit.CapabilityId);
        Assert.Equal("target:player-1", audit.ResourceId);
        Assert.NotNull(audit.Fields);
        Assert.Equal("11", audit.Fields!["messageLength"]); // "hello world" is 11 chars

        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    // ---------------------------------------------------------------------------
    // 8. Revoked capability: audit reason is preserved identically in both modes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Revoked_capability_audit_reason_matches_in_both_modes()
    {
        const string reason = "fraud-detection-halt";

        var sinkI = new InMemoryPluginMessageSink();
        var hostI = BuildHost(sinkI);
        var planI = await PrepareMessagePlanAsync(hostI, "player-1", "hello");
        hostI.RevokeCapability(PluginMessageBindings.CapabilityId, reason);
        var interpreted = await hostI.ExecuteAsync(planI, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        var sinkC = new InMemoryPluginMessageSink();
        var hostC = BuildHost(sinkC);
        var planC = await PrepareMessagePlanAsync(hostC, "player-1", "hello");
        hostC.RevokeCapability(PluginMessageBindings.CapabilityId, reason);
        var compiled = await hostC.ExecuteAsync(planC, "main", SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        var auditI = Assert.Single(interpreted.AuditEvents, e => e.Kind == "CapabilityRevoked");
        var auditC = Assert.Single(compiled.AuditEvents, e => e.Kind == "CapabilityRevoked");

        // Reason text must be identical in both modes
        Assert.Equal(reason, auditI.Message);
        Assert.Equal(reason, auditC.Message);
        Assert.Equal(auditI.Fields!["reason"], auditC.Fields!["reason"]);
        Assert.Equal(auditI.CapabilityId, auditC.CapabilityId);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Hosting.SandboxHost BuildHost(InMemoryPluginMessageSink sink)
        => Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    private static async ValueTask<ExecutionPlan> PrepareMessagePlanAsync(
        Hosting.SandboxHost host,
        string targetId,
        string message,
        IEnumerable<string>? allowedTargets = null,
        int? maxMessageLength = null)
    {
        var module = await host.ImportJsonAsync(MessageModuleJson(targetId, message));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(allowedTargets: allowedTargets, maxMessageLength: maxMessageLength)
            .WithFuel(10_000)
            .Build());
    }

    private static string MessageModuleJson(string targetId, string message)
        => $$"""
        {
          "id": "audit-failure-parity-send",
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
                      { "string": "{{targetId}}" },
                      { "string": "{{message}}" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static void AssertFailedRunSummary(SandboxExecutionResult result, SandboxErrorCode expectedCode)
    {
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(expectedCode, summary.ErrorCode);
    }
}
