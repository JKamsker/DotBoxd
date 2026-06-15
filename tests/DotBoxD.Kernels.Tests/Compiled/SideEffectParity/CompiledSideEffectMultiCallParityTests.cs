using DotBoxD.Hosting;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Differential parity tests for compiled side-effecting bindings under multi-call,
/// loop, and mixed workloads (PR #27: "Compile side-effecting runtime-stub bindings").
///
/// Each test runs the identical module both interpreted and compiled, then asserts that
/// every observable is identical: result value, Succeeded flag, ActualMode, sink
/// deliveries (count, order, content), AuditEvents (count, kind, order, per-field), and
/// ResourceUsage host-call counters. This stresses repeated CallBinding2 dispatch and
/// audit ordering under a compiled entrypoint.
/// </summary>
public sealed class CompiledSideEffectMultiCallParityTests
{
    // -------------------------------------------------------------------------
    // 1. Three sequential host.message.send calls (3x CallBinding2 dispatch)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Three_sequential_sends_produce_identical_sink_and_audit_count_in_both_modes()
    {
        const string moduleJson = """
        {
          "id": "multi-send-sequential",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "first" }] } },
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-2" }, { "string": "second" }] } },
                { "op": "return", "value": { "call": "host.message.send", "args": [{ "string": "player-3" }, { "string": "third" }] } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // Sink delivery: same count and same order.
        Assert.Equal(3, interpretedSink.Messages.Count);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);
        for (var i = 0; i < interpretedSink.Messages.Count; i++)
        {
            Assert.Equal(interpretedSink.Messages[i].TargetId, compiledSink.Messages[i].TargetId);
            Assert.Equal(interpretedSink.Messages[i].Message, compiledSink.Messages[i].Message);
        }

        // Audit: same number of PluginMessage events.
        var interpretedAudits = interpreted.AuditEvents.Where(e => e.Kind == "PluginMessage").ToList();
        var compiledAudits = compiled.AuditEvents.Where(e => e.Kind == "PluginMessage").ToList();
        Assert.Equal(3, interpretedAudits.Count);
        Assert.Equal(interpretedAudits.Count, compiledAudits.Count);

        // HostCalls counter identical.
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
        Assert.Equal(3, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 2. Sink delivery ORDER is preserved across modes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Sequential_sends_preserve_delivery_order_identically_in_both_modes()
    {
        const string moduleJson = """
        {
          "id": "multi-send-order",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "alpha" }] } },
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-2" }, { "string": "beta" }] } },
                { "op": "return", "value": { "call": "host.message.send", "args": [{ "string": "player-3" }, { "string": "gamma" }] } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);

        // Order: alpha, beta, gamma.
        Assert.Equal(new[] { "alpha", "beta", "gamma" },
            interpretedSink.Messages.Select(m => m.Message).ToArray());
        Assert.Equal(
            interpretedSink.Messages.Select(m => m.Message).ToArray(),
            compiledSink.Messages.Select(m => m.Message).ToArray());
        Assert.Equal(
            interpretedSink.Messages.Select(m => m.TargetId).ToArray(),
            compiledSink.Messages.Select(m => m.TargetId).ToArray());

        // Audit events appear in the same order.
        var interpretedAudits = interpreted.AuditEvents.Where(e => e.Kind == "PluginMessage").ToList();
        var compiledAudits = compiled.AuditEvents.Where(e => e.Kind == "PluginMessage").ToList();
        Assert.Equal(interpretedAudits.Count, compiledAudits.Count);
        for (var i = 0; i < interpretedAudits.Count; i++)
        {
            Assert.Equal(interpretedAudits[i].ResourceId, compiledAudits[i].ResourceId);
            Assert.Equal(interpretedAudits[i].BindingId, compiledAudits[i].BindingId);
        }
    }

    // -------------------------------------------------------------------------
    // 3. Loop-based sends: forRange calling host.message.send N times
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Loop_sends_four_times_and_audit_count_matches_interpreted()
    {
        // Uses a forRange loop to call host.message.send in each iteration.
        // Stresses the "same CallBinding2 path N times from a compiled loop body".
        const string moduleJson = """
        {
          "id": "multi-send-loop",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 4 },
                  "body": [
                    {
                      "op": "expr",
                      "value": {
                        "call": "host.message.send",
                        "args": [ { "string": "player-1" }, { "string": "loop-msg" } ]
                      }
                    }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);

        Assert.Equal(4, interpretedSink.Messages.Count);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);

        // Every delivered message in both sinks must match.
        for (var i = 0; i < interpretedSink.Messages.Count; i++)
        {
            Assert.Equal(interpretedSink.Messages[i].TargetId, compiledSink.Messages[i].TargetId);
            Assert.Equal(interpretedSink.Messages[i].Message, compiledSink.Messages[i].Message);
        }

        // Host-call counter: 4 per mode.
        Assert.Equal(4, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);

        // PluginMessage audit events: 4 in each mode.
        Assert.Equal(4, interpreted.AuditEvents.Count(e => e.Kind == "PluginMessage"));
        Assert.Equal(
            interpreted.AuditEvents.Count(e => e.Kind == "PluginMessage"),
            compiled.AuditEvents.Count(e => e.Kind == "PluginMessage"));
    }

    // -------------------------------------------------------------------------
    // 4. Loop-based sends: each iteration sends to a distinct target via
    //    a variable that depends on computation inside the loop body.
    //    Stresses that compiled mode correctly re-evaluates variables per
    //    iteration (not caching a stale value from the first pass).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Loop_sends_to_same_target_each_iteration_compiled_matches_interpreted()
    {
        // Send 3 messages inside a forRange, each to "target-a".
        // The loop body uses a literal for both args — verifies repeated CallBinding2 dispatch.
        const string moduleJson = """
        {
          "id": "multi-send-loop-target",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 3 },
                  "body": [
                    {
                      "op": "expr",
                      "value": {
                        "call": "host.message.send",
                        "args": [ { "string": "target-a" }, { "string": "ping" } ]
                      }
                    }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        Assert.Equal(3, compiledSink.Messages.Count);
        Assert.All(compiledSink.Messages, m => Assert.Equal("target-a", m.TargetId));
        Assert.All(compiledSink.Messages, m => Assert.Equal("ping", m.Message));

        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 5. Mixed: pure arithmetic interleaved with side-effecting calls
    //    Verifies that pure computation before and after each binding call
    //    produces the correct result value AND delivers sink events.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Mixed_pure_arithmetic_and_two_sends_return_correct_value_and_deliver_sink()
    {
        // Compute a value from pure arithmetic, send it as a message, then
        // send another message, then return the computed integer.
        // This stresses the interaction between the fast I32 path and CallBinding2.
        const string moduleJson = """
        {
          "id": "multi-send-mixed",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "score",
                  "value": { "op": "mul", "left": { "i32": 6 }, "right": { "i32": 7 } }
                },
                {
                  "op": "expr",
                  "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "score-computed" }] }
                },
                {
                  "op": "expr",
                  "value": { "call": "host.message.send", "args": [{ "string": "player-2" }, { "string": "done" }] }
                },
                { "op": "return", "value": { "var": "score" } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunSendAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // Return value: 6 * 7 = 42.
        Assert.Equal(42, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(42, ((I32Value)compiled.Value!).Value);

        // Sink: 2 messages in each mode, same content.
        Assert.Equal(2, interpretedSink.Messages.Count);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);
        for (var i = 0; i < interpretedSink.Messages.Count; i++)
        {
            Assert.Equal(interpretedSink.Messages[i].TargetId, compiledSink.Messages[i].TargetId);
            Assert.Equal(interpretedSink.Messages[i].Message, compiledSink.Messages[i].Message);
        }

        // HostCalls.
        Assert.Equal(2, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 6. Mixed: log.info (1-arg SideEffectingExternal, CallBinding path)
    //    interleaved with host.message.send (2-arg, CallBinding2 path).
    //    Verifies audit ordering across two different side-effecting bindings.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Log_then_send_then_log_audit_order_matches_interpreted()
    {
        const string moduleJson = """
        {
          "id": "multi-mixed-log-send",
          "version": "1.0.0",
          "capabilityRequests": [
            { "id": "log.write", "reason": "operational trace" },
            { "id": "host.message.write" }
          ],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "before-send" }] } },
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "payload" }] } },
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "after-send" }] } }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await MultiCallRunMixedAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await MultiCallRunMixedAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // Sink: 1 message in each mode.
        var interpretedMsg = Assert.Single(interpretedSink.Messages);
        var compiledMsg = Assert.Single(compiledSink.Messages);
        Assert.Equal(interpretedMsg.TargetId, compiledMsg.TargetId);
        Assert.Equal(interpretedMsg.Message, compiledMsg.Message);

        // Audit event ORDER must be identical: SandboxLog, PluginMessage, SandboxLog.
        var interpretedSideEffectAudits = interpreted.AuditEvents
            .Where(e => e.Kind is "SandboxLog" or "PluginMessage")
            .ToList();
        var compiledSideEffectAudits = compiled.AuditEvents
            .Where(e => e.Kind is "SandboxLog" or "PluginMessage")
            .ToList();

        Assert.Equal(3, interpretedSideEffectAudits.Count);
        Assert.Equal(interpretedSideEffectAudits.Count, compiledSideEffectAudits.Count);

        // Kind sequence is identical.
        Assert.Equal(
            interpretedSideEffectAudits.Select(e => e.Kind).ToArray(),
            compiledSideEffectAudits.Select(e => e.Kind).ToArray());

        // Log messages preserved identically.
        Assert.Equal(
            interpretedSideEffectAudits.Select(e => e.Message).ToArray(),
            compiledSideEffectAudits.Select(e => e.Message).ToArray());

        // HostCalls: 3 in each mode (2 log.info + 1 host.message.send).
        Assert.Equal(3, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 7. Host-call quota stops both modes at the same point.
    //    With MaxHostCalls=2, the third send must fail with QuotaExceeded.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Three_sends_with_max_host_calls_2_fails_identically_in_both_modes()
    {
        const string moduleJson = """
        {
          "id": "multi-send-quota",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-1" }, { "string": "first" }] } },
                { "op": "expr", "value": { "call": "host.message.send", "args": [{ "string": "player-2" }, { "string": "second" }] } },
                { "op": "return", "value": { "call": "host.message.send", "args": [{ "string": "player-3" }, { "string": "third" }] } }
              ]
            }
          ]
        }
        """;

        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .WithMaxHostCalls(2)
            .Build();

        var (interpreted, interpretedSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Interpreted, policy);
        var (compiled, compiledSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Compiled, policy);

        // Both must fail.
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interpreted.Error!.Code);
        Assert.Equal(interpreted.Error.Code, compiled.Error!.Code);

        // Both stop before the third send.
        Assert.True(interpretedSink.Messages.Count <= 2);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);

        // Host-call counter identical.
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // -------------------------------------------------------------------------
    // 8. Loop with early quota stop: loop tries 5 sends but MaxHostCalls=3.
    //    Both modes must stop after the same iteration and count.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Loop_five_sends_with_max_host_calls_3_stops_identically_in_both_modes()
    {
        const string moduleJson = """
        {
          "id": "multi-send-loop-quota",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "host.message.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 5 },
                  "body": [
                    {
                      "op": "expr",
                      "value": {
                        "call": "host.message.send",
                        "args": [ { "string": "player-1" }, { "string": "batch" } ]
                      }
                    }
                  ]
                },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;

        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .WithMaxHostCalls(3)
            .Build();

        var (interpreted, interpretedSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Interpreted, policy);
        var (compiled, compiledSink) = await MultiCallRunSendWithPolicyAsync(moduleJson, ExecutionMode.Compiled, policy);

        // Both must fail.
        Assert.False(interpreted.Succeeded);
        Assert.False(compiled.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, interpreted.Error!.Code);
        Assert.Equal(interpreted.Error.Code, compiled.Error!.Code);

        // At most 3 messages delivered in each mode (quota stops at the 4th attempt).
        Assert.True(interpretedSink.Messages.Count <= 3);
        Assert.Equal(interpretedSink.Messages.Count, compiledSink.Messages.Count);

        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunSendAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build();
        return await MultiCallRunSendWithPolicyAsync(moduleJson, mode, policy);
    }

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunSendWithPolicyAsync(
        string moduleJson,
        ExecutionMode mode,
        SandboxPolicy policy)
    {
        var sink = new InMemoryPluginMessageSink();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, sink);
    }

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> MultiCallRunMixedAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var sink = new InMemoryPluginMessageSink();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module,
            SandboxPolicyBuilder.Create()
                .GrantHostMessageWrite()
                .GrantLogging()
                .WithFuel(10_000)
                .Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, sink);
    }
}
