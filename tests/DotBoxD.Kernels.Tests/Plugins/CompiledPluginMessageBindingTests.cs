using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Compiled-mode coverage for host.message.send. Under #27 the SideEffectingExternal binding compiles
/// instead of falling back to the interpreter, so these tests assert the compiled path preserves
/// capability enforcement, audit, and sink delivery — matching the interpreter. Split out of
/// PluginMessageBindingTests to keep each file under the 500-line file-length gate.
/// </summary>
public sealed class CompiledPluginMessageBindingTests
{
    [Fact]
    public async Task Plugin_message_binding_compiles_without_interpreter_fallback()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "plugin-message-compiled",
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
                      { "string": "compiled message" }
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

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal("compiled message", message.Message);
        Assert.Contains(result.AuditEvents, e => e.Kind == "PluginMessage");
    }

    [Fact]
    public async Task Compiled_mode_plugin_message_send_preserves_audit_and_sink_through_refactored_invoker()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "plugin-message-compiled",
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
                      { "string": "token=abc123\nBearer secret-value" }
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

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);

        // #27 removes the effects gate, so host.message.send (SideEffectingExternal) now compiles
        // instead of falling back to the interpreter. Capability, quota, return-charging, and audit
        // emission ride entirely on CompiledBindingDispatcher.CallBinding2, so the compiled path must
        // still redact and deliver exactly as the interpreter does.
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal("token=abc123 Bearer secret-value", Assert.Single(messages.Messages).Message);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.Equal("token=[redacted] Bearer [redacted]", audit.Message);
        Assert.Equal("32", audit.Fields!["messageLength"]);
        Assert.Equal("target:player-1", audit.ResourceId);
    }

    [Fact]
    public async Task Compiled_host_message_send_audit_matches_interpreted()
    {
        // The audit-parity gate for #27: now that host.message.send compiles, its capability check,
        // quota, redaction, and audit emission all run through the compiled dispatcher. Running the
        // same module interpreted and compiled must produce an identical PluginMessage audit event and
        // identical sink delivery — otherwise the compiled path would be a weaker oversight surface.
        const string moduleJson = """
        {
          "id": "plugin-message-parity",
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
                      { "string": "token=abc123\nBearer secret-value" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;

        var (interpreted, interpretedSink) = await RunSendAsync(moduleJson, ExecutionMode.Interpreted);
        var (compiled, compiledSink) = await RunSendAsync(moduleJson, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);

        // Sink delivery is identical (clean payload, never redacted).
        var interpretedMessage = Assert.Single(interpretedSink.Messages);
        var compiledMessage = Assert.Single(compiledSink.Messages);
        Assert.Equal(interpretedMessage.TargetId, compiledMessage.TargetId);
        Assert.Equal(interpretedMessage.Message, compiledMessage.Message);

        // Audit event is field-for-field identical (redaction, length, resource, capability, effect).
        var interpretedAudit = Assert.Single(interpreted.AuditEvents, e => e.Kind == "PluginMessage");
        var compiledAudit = Assert.Single(compiled.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.Equal(interpretedAudit.BindingId, compiledAudit.BindingId);
        Assert.Equal(interpretedAudit.CapabilityId, compiledAudit.CapabilityId);
        Assert.Equal(interpretedAudit.Effect, compiledAudit.Effect);
        Assert.Equal(interpretedAudit.ResourceId, compiledAudit.ResourceId);
        Assert.Equal(interpretedAudit.Message, compiledAudit.Message);
        Assert.Equal(interpretedAudit.Fields!["messageLength"], compiledAudit.Fields!["messageLength"]);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Host_message_send_denied_identically_in_both_modes_after_capability_revocation(ExecutionMode mode)
    {
        // Capability-bypass guard: a revoked capability must block the side effect in BOTH modes.
        // If the compiled path ever let a revoked host.message.send through (or delivered to the sink),
        // the Compiled case fails here — that is exactly the "capability bypass under load" scenario.
        const string moduleJson = """
        {
          "id": "plugin-message-revoked",
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
                    "args": [ { "string": "player-1" }, { "string": "should not arrive" } ]
                  }
                }
              ]
            }
          ]
        }
        """;
        var messages = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());

        host.RevokeCapability(PluginMessageBindings.CapabilityId, "disabled for test");

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PolicyDenied, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    private static async Task<(SandboxExecutionResult Result, InMemoryPluginMessageSink Sink)> RunSendAsync(
        string moduleJson,
        ExecutionMode mode)
    {
        var messages = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build());
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        return (result, messages);
    }
}
