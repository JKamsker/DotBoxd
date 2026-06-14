using DotBoxd.Plugins;
using DotBoxd.Kernels.PluginLocal;

namespace DotBoxd.Kernels.Tests;

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
}
