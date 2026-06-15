using DotBoxD.Plugins;
using DotBoxD.Kernels.PluginLocal;

namespace DotBoxD.Kernels.Tests;

public sealed class PluginMessageBindingTests
{
    [Fact]
    public async Task Kernel_handler_capability_is_required_by_policy()
    {
        var server = PluginServer.Create();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(FireDamagePluginPackage.Create(), policy).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
    }

    [Fact]
    public async Task Plugin_message_binding_rejects_invalid_target_id_before_sink_send()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "plugin-message-target",
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

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Plugin_message_binding_redacts_audit_message_without_changing_sink_payload()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "plugin-message-redaction",
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

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("token=abc123 Bearer secret-value", Assert.Single(messages.Messages).Message);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.Equal("token=[redacted] Bearer [redacted]", audit.Message);
        Assert.Equal("32", audit.Fields!["messageLength"]);
    }

    [Fact]
    public async Task Plugin_message_binding_redacts_audit_target_without_changing_sink_target()
    {
        var messages = new InMemoryPluginMessageSink();
        var host = Hosting.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync("""
        {
          "id": "plugin-message-target-redaction",
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
                      { "string": "token:abc123" },
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

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal("token:abc123", Assert.Single(messages.Messages).TargetId);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.Equal("target:[redacted]", audit.ResourceId);
        Assert.DoesNotContain("abc123", audit.ResourceId);
        Assert.DoesNotContain("token:", audit.ResourceId);
    }

    [Fact]
    public async Task Plugin_message_binding_does_not_copy_clean_sink_payload()
    {
        var sink = new CapturingMessageSink();
        var binding = PluginMessageBindings.CreateSend(sink);
        var message = string.Concat("clean", " payload");

        await binding.Invoke(
            MessageContext(binding),
            [SandboxValue.FromString("player-1"), SandboxValue.FromString(message)],
            CancellationToken.None);

        Assert.Same(message, sink.Message);
    }

    [Fact]
    public async Task Plugin_message_binding_still_sanitizes_control_characters_for_sink_payload()
    {
        var sink = new CapturingMessageSink();
        var binding = PluginMessageBindings.CreateSend(sink);
        var message = "line-one\nline-two";

        await binding.Invoke(
            MessageContext(binding),
            [SandboxValue.FromString("player-1"), SandboxValue.FromString(message)],
            CancellationToken.None);

        Assert.Equal("line-one line-two", sink.Message);
        Assert.NotSame(message, sink.Message);
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

        // host.message.send is SideEffectingExternal, so the compiler effects gate keeps the
        // entrypoint on the interpreter via fallback on this branch. This pins that the refactored
        // PluginMessageSendInvoker still audits and delivers correctly when compiled mode is
        // requested; the ActualMode assertion will flip if #27 enables compiling descriptor-governed
        // side-effecting bindings, forcing this test to be revisited together with that change.
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
        Assert.Equal("token=abc123 Bearer secret-value", Assert.Single(messages.Messages).Message);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "PluginMessage");
        Assert.Equal("token=[redacted] Bearer [redacted]", audit.Message);
        Assert.Equal("32", audit.Fields!["messageLength"]);
        Assert.Equal("target:player-1", audit.ResourceId);
    }

    [Fact]
    public async Task Compiled_only_plugin_message_send_cannot_compile_side_effecting_binding_without_fallback()
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
          "id": "plugin-message-compiled-only",
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

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        // The effects gate refuses to compile a side-effecting binding and fallback is disabled, so the
        // run fails closed and never reaches the sink. Guards that #28's fast invoker did not silently
        // make host.message.send compilable on its own (that is #27's policy change).
        Assert.False(result.Succeeded);
        Assert.Empty(messages.Messages);
    }

    private static SandboxContext MessageContext(BindingDescriptor binding)
    {
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(10_000)
            .Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([binding]),
            new InMemoryAuditSink(),
            CancellationToken.None,
            moduleHash: "module",
            policyHash: "policy");
    }

    private sealed class CapturingMessageSink : IPluginMessageSink
    {
        public string? TargetId { get; private set; }
        public string? Message { get; private set; }

        public ValueTask SendAsync(
            string targetId,
            string message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TargetId = targetId;
            Message = message;
            return ValueTask.CompletedTask;
        }
    }
}
