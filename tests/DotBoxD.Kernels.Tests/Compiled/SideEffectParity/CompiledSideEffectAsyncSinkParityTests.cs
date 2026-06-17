using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

/// <summary>
/// Parity tests for the async-sink dimension: verifies that modules exercising
/// host.message.send produce identical observable outcomes (result value, Succeeded,
/// ActualMode, sink deliveries, AuditEvents, ResourceUsage, Error.Code) under both
/// the interpreter and the compiled runtime.
///
/// On this branch (PR #27), the compiled path executes side-effecting entrypoints
/// (host.message.send, log.info, file.writeText) directly through the generic CallBinding
/// dispatcher rather than rejecting them. The compiled path now MATCHES the interpreter.
/// Tests confirm:
///
/// 1. Async-yield sink: both interpreted and compiled (AllowFallback=false) deliver once
///    with matching audit through the compiled async worker pump.
/// 2. Async-yield sink: compiled run does NOT fall back (ActualMode==Compiled) and matches
///    the interpreted run on delivery, audit, and ResourceUsage.
/// 3. Throwing async sink: maps to BindingFailure in BOTH modes (same error code, no leak).
/// 4. Multiple sends: interpreted delivers all messages in order correctly.
/// 5. Audit events on interpreted path: structurally correct and complete.
/// 6. Revoked capability: both modes deny with PolicyDenied (revocation runs before
///    the compiled artifact executes, so both modes reject consistently).
/// 7. OperationCanceledException from sink: maps to BindingFailure on the interpreted path.
/// </summary>
public sealed class CompiledSideEffectAsyncSinkParityTests
{
    // -----------------------------------------------------------------------
    // Test 1: async-yield sink delivers exactly once under BOTH interpreted and
    //         compiled (no fallback) modes. PR #27 runs the side-effecting binding
    //         through the generic CallBinding dispatcher; the #31 fix now drives
    //         pending compiled awaits through the compiled async worker pump.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_yield_sink_delivers_once_with_matching_audit_in_interpreted_and_compiled()
    {
        // Arrange
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var compiledSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var host = CreateHost(interpretedSink);
        var compiledHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await host.ImportJsonAsync(MessageSendModule("async-yield-parity-i"));
        var iPlan = await host.PrepareAsync(iModule, policy);
        var cModule = await compiledHost.ImportJsonAsync(MessageSendModule("async-yield-parity-c"));
        var cPlan = await compiledHost.PrepareAsync(cModule, policy);

        // Act — interpreted run
        var interpretedResult = await host.ExecuteAsync(
            iPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Act — compiled run (no fallback): the side-effecting binding executes compiled.
        var compiledResult = await compiledHost.ExecuteAsync(
            cPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Assert — both modes succeed
        Assert.True(interpretedResult.Succeeded, interpretedResult.Error?.SafeMessage);
        Assert.True(compiledResult.Succeeded, compiledResult.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interpretedResult.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiledResult.ActualMode);

        // Assert — each mode delivered the message exactly once, identically.
        var iMsg = Assert.Single(interpretedSink.Messages);
        var cMsg = Assert.Single(compiledSink.Messages);
        Assert.Equal(iMsg.TargetId, cMsg.TargetId);
        Assert.Equal(iMsg.Message, cMsg.Message);
        Assert.Equal("player-1", cMsg.TargetId);
        Assert.Equal("hello", cMsg.Message);

        // Assert — PluginMessage audit parity.
        var iAudit = Assert.Single(interpretedResult.AuditEvents, e => e.Kind == "PluginMessage");
        var cAudit = Assert.Single(compiledResult.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.Equal(iAudit.Success, cAudit.Success);
        Assert.Equal(iAudit.BindingId, cAudit.BindingId);
        Assert.Equal(iAudit.CapabilityId, cAudit.CapabilityId);
        Assert.Equal(iAudit.ResourceId, cAudit.ResourceId);
        Assert.Equal(iAudit.Message, cAudit.Message);

        // Assert — HostCalls parity.
        Assert.Equal(interpretedResult.ResourceUsage.HostCalls, compiledResult.ResourceUsage.HostCalls);
    }

    // -----------------------------------------------------------------------
    // Test 2: async-yield sink runs compiled in a fallback-enabled compiled host
    //         and delivers messages identically to the plain interpreted host.
    //         PR #27: the effectful module compiles directly, so the compiled run
    //         does NOT fall back — ActualMode==Compiled.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_yield_sink_compiled_delivers_identical_to_pure_interpreted()
    {
        // Arrange — both hosts share the same module JSON; each has its own sink
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var compiledSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var interpretedHost = CreateHost(interpretedSink);
        var compiledHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await interpretedHost.ImportJsonAsync(MessageSendModule("compiled-parity-i"));
        var iPlan = await interpretedHost.PrepareAsync(iModule, policy);
        var cModule = await compiledHost.ImportJsonAsync(MessageSendModule("compiled-parity-c"));
        var cPlan = await compiledHost.PrepareAsync(cModule, policy);

        // Act — explicit interpreted
        var iResult = await interpretedHost.ExecuteAsync(
            iPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Act — compiled with fallback allowed: the effectful module compiles directly,
        // so it runs compiled rather than falling back to the interpreter.
        var cResult = await compiledHost.ExecuteAsync(
            cPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = true });

        // Assert — both runs succeeded
        Assert.True(iResult.Succeeded, iResult.Error?.SafeMessage);
        Assert.True(cResult.Succeeded, cResult.Error?.SafeMessage);

        // Assert — compiled run genuinely ran compiled (did NOT fall back).
        Assert.Equal(ExecutionMode.Interpreted, iResult.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, cResult.ActualMode);

        // Assert — message delivery parity: async yield sink delivered once in each
        var iMsg = Assert.Single(interpretedSink.Messages);
        var cMsg = Assert.Single(compiledSink.Messages);
        Assert.Equal(iMsg.TargetId, cMsg.TargetId);
        Assert.Equal(iMsg.Message, cMsg.Message);
        Assert.Equal("player-1", cMsg.TargetId);
        Assert.Equal("hello", cMsg.Message);

        // Assert — PluginMessage audit parity
        var iAudit = Assert.Single(iResult.AuditEvents, e => e.Kind == "PluginMessage");
        var cAudit = Assert.Single(cResult.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.True(iAudit.Success);
        Assert.True(cAudit.Success);
        Assert.Equal(iAudit.BindingId, cAudit.BindingId);
        Assert.Equal(iAudit.CapabilityId, cAudit.CapabilityId);
        Assert.Equal(iAudit.ResourceId, cAudit.ResourceId);
        Assert.Equal(iAudit.Message, cAudit.Message);

        // Assert — HostCalls parity
        Assert.Equal(iResult.ResourceUsage.HostCalls, cResult.ResourceUsage.HostCalls);
    }

    // -----------------------------------------------------------------------
    // Test 3: throwing sink — PR #27 runs the binding compiled, so a throwing
    //         sink maps to BindingFailure in BOTH interpreted and compiled modes
    //         (the binding is reached and crashes in each). The redacted message
    //         never leaks the host failure detail, and no delivery occurs.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Throwing_sink_returns_BindingFailure_in_both_interpreted_and_compiled()
    {
        // Arrange
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_ThrowingSink("simulated host failure");
        var compiledSink = new AsyncSinkAsyncSinkParityTests_ThrowingSink("simulated host failure");
        var iHost = CreateHost(interpretedSink);
        var cHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await iHost.ImportJsonAsync(MessageSendModule("throwing-sink-parity-i"));
        var iPlan = await iHost.PrepareAsync(iModule, policy);
        var cModule = await cHost.ImportJsonAsync(MessageSendModule("throwing-sink-parity-c"));
        var cPlan = await cHost.PrepareAsync(cModule, policy);

        // Act
        var iResult = await iHost.ExecuteAsync(
            iPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        var cResult = await cHost.ExecuteAsync(
            cPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Assert — interpreted fails: binding threw, mapped to BindingFailure
        Assert.False(iResult.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, iResult.Error!.Code);
        Assert.DoesNotContain("simulated host failure", iResult.Error.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(interpretedSink.Messages);
        Assert.Equal(ExecutionMode.Interpreted, iResult.ActualMode);

        // Assert — compiled reaches the same binding and crashes the same way → BindingFailure
        Assert.False(cResult.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, cResult.Error!.Code);
        Assert.DoesNotContain("simulated host failure", cResult.Error.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(compiledSink.Messages);
        Assert.Equal(ExecutionMode.Compiled, cResult.ActualMode);

        // True parity: identical error code in both modes.
        Assert.Equal(iResult.Error.Code, cResult.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Test 4: multiple sends in interpreted mode — all deliver in order with
    //         correct PluginMessage audit events.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_yield_sink_with_multiple_sends_delivers_all_in_order_interpreted()
    {
        // Arrange
        var sink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var host = CreateHost(sink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var module = await host.ImportJsonAsync(MultiSendModule("multi-send-yield"));
        var plan = await host.PrepareAsync(module, policy);

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Assert — succeeds
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);

        // Assert — two messages delivered in order via async-yield path
        Assert.Equal(2, sink.Messages.Count);
        Assert.Equal("player-1", sink.Messages[0].TargetId);
        Assert.Equal("first", sink.Messages[0].Message);
        Assert.Equal("player-2", sink.Messages[1].TargetId);
        Assert.Equal("second", sink.Messages[1].Message);

        // Assert — two PluginMessage audit events, both successful
        var pluginAudits = result.AuditEvents.Where(e => e.Kind == "PluginMessage").ToList();
        Assert.Equal(2, pluginAudits.Count);
        Assert.All(pluginAudits, a => Assert.True(a.Success));
        Assert.Equal(2, result.ResourceUsage.HostCalls);
    }

    // -----------------------------------------------------------------------
    // Test 5: audit event fields are complete and correct on the interpreted path
    //         (BindingId, CapabilityId, ResourceId, required Fields keys).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_yield_sink_audit_event_fields_are_complete_under_interpreter()
    {
        // Arrange
        var sink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var host = CreateHost(sink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var module = await host.ImportJsonAsync(MessageSendModule("audit-field-check"));
        var plan = await host.PrepareAsync(module, policy);

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);

        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");

        // Assert — binding and capability ids
        Assert.Equal("host.message.send", audit.BindingId);
        Assert.Equal("host.message.write", audit.CapabilityId);

        // Assert — success, no error code
        Assert.True(audit.Success);
        Assert.Null(audit.ErrorCode);

        // Assert — resource id is present
        Assert.False(string.IsNullOrWhiteSpace(audit.ResourceId));

        // Assert — required audit Fields present
        Assert.NotNull(audit.Fields);
        Assert.True(audit.Fields!.ContainsKey("resourceKind"), "resourceKind must be present");
        Assert.True(audit.Fields.ContainsKey("moduleHash"), "moduleHash must be present");
        Assert.True(audit.Fields.ContainsKey("policyHash"), "policyHash must be present");
        Assert.True(audit.Fields.ContainsKey("messageLength"), "messageLength must be present");
        Assert.Equal("5", audit.Fields["messageLength"]); // "hello".Length == 5
    }

    // -----------------------------------------------------------------------
    // Test 6: revoked capability — both interpreted and compiled (no-fallback)
    //         return PolicyDenied. Revocation check runs before the compiler gate.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Revoked_capability_produces_PolicyDenied_in_interpreted_and_compiled()
    {
        // Arrange
        var interpretedSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var compiledSink = new AsyncSinkAsyncSinkParityTests_AsyncYieldSink();
        var iHost = CreateHost(interpretedSink);
        var cHost = CreateCompiledHost(compiledSink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var iModule = await iHost.ImportJsonAsync(MessageSendModule("revocation-parity-i"));
        var iPlan = await iHost.PrepareAsync(iModule, policy);
        var cModule = await cHost.ImportJsonAsync(MessageSendModule("revocation-parity-c"));
        var cPlan = await cHost.PrepareAsync(cModule, policy);

        // Revoke before execution
        iHost.RevokeCapability(PluginMessageBindings.CapabilityId, "parity-test-revoked");
        cHost.RevokeCapability(PluginMessageBindings.CapabilityId, "parity-test-revoked");

        // Act
        var iResult = await iHost.ExecuteAsync(
            iPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        var cResult = await cHost.ExecuteAsync(
            cPlan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // Assert — both fail with PolicyDenied
        Assert.False(iResult.Succeeded);
        Assert.False(cResult.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, iResult.Error!.Code);
        Assert.Equal(SandboxErrorCode.PolicyDenied, cResult.Error!.Code);

        // Assert — no messages delivered to either sink
        Assert.Empty(interpretedSink.Messages);
        Assert.Empty(compiledSink.Messages);

        // Assert — CapabilityRevoked audit in interpreted result
        var iRevoke = Assert.Single(iResult.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.False(iRevoke.Success);
        Assert.Equal(PluginMessageBindings.CapabilityId, iRevoke.CapabilityId);
        Assert.Equal("parity-test-revoked", iRevoke.Message);

        // Assert — CapabilityRevoked audit in compiled result
        var cRevoke = Assert.Single(cResult.AuditEvents, e => e.Kind == "CapabilityRevoked");
        Assert.False(cRevoke.Success);
        Assert.Equal(PluginMessageBindings.CapabilityId, cRevoke.CapabilityId);
        Assert.Equal("parity-test-revoked", cRevoke.Message);
    }

    // -----------------------------------------------------------------------
    // Test 7: OperationCanceledException from async sink (host-side private token) —
    //         interpreted maps this to BindingFailure; the error does not leak
    //         implementation details.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Async_sink_throwing_OperationCanceledException_with_private_token_maps_to_BindingFailure()
    {
        // Arrange — OCE from a private CTS, NOT the sandbox's token
        var sink = new AsyncSinkAsyncSinkParityTests_CanceledSink();
        var host = CreateHost(sink);
        var policy = SandboxPolicyBuilder.Create().GrantHostMessageWrite().WithFuel(10_000).Build();

        var module = await host.ImportJsonAsync(MessageSendModule("oce-private-token"));
        var plan = await host.PrepareAsync(module, policy);

        // Act
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        // Assert — fails but maps to BindingFailure (private OCE is not sandbox cancellation)
        Assert.False(result.Succeeded);
        // CompiledBindingDispatcher catches OCE not from sandbox CT and maps to BindingFailure.
        // The interpreter routes through the same binding invocation path.
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Empty(sink.Messages);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static SandboxHost CreateHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
        });

    private static SandboxHost CreateCompiledHost(IPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    /// <summary>
    /// Module that calls host.message.send once with ("player-1", "hello").
    /// </summary>
    private static string MessageSendModule(string id) => $$"""
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
                      { "string": "hello" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    /// <summary>
    /// Module that calls host.message.send twice sequentially:
    /// ("player-1", "first") then ("player-2", "second").
    /// </summary>
    private static string MultiSendModule(string id) => $$"""
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
                  "op": "expr",
                  "value": {
                    "call": "host.message.send",
                    "args": [
                      { "string": "player-1" },
                      { "string": "first" }
                    ]
                  }
                },
                {
                  "op": "return",
                  "value": {
                    "call": "host.message.send",
                    "args": [
                      { "string": "player-2" },
                      { "string": "second" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

    // -----------------------------------------------------------------------
    // Nested sink implementations — prefixed to avoid collision when files merge
    // -----------------------------------------------------------------------

    /// <summary>
    /// A sink whose SendAsync always yields to the thread pool before recording,
    /// so the CompiledBindingDispatcher pending-await path is exercised on any
    /// code path that invokes the binding.
    /// </summary>
    private sealed class AsyncSinkAsyncSinkParityTests_AsyncYieldSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            // Force genuine async completion — ValueTask is NOT already complete.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Add(new PluginMessage(targetId, message));
        }
    }

    /// <summary>
    /// A sink that throws an InvalidOperationException from SendAsync,
    /// simulating a misbehaving external dependency.
    /// </summary>
    private sealed class AsyncSinkAsyncSinkParityTests_ThrowingSink(string detail) : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(detail);
    }

    /// <summary>
    /// A sink that throws OperationCanceledException using its own private CancellationToken
    /// (NOT the sandbox's token), simulating a host-side timeout or abort.
    /// </summary>
    private sealed class AsyncSinkAsyncSinkParityTests_CanceledSink : IPluginMessageSink
    {
        private readonly List<PluginMessage> _messages = [];

        public IReadOnlyList<PluginMessage> Messages => _messages.AsReadOnly();

        public async ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            // Cancel with a private token — simulates host-side abort, NOT sandbox cancellation.
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
        }
    }
}
