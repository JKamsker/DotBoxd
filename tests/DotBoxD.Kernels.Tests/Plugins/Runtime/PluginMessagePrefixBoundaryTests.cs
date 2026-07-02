using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginMessagePrefixBoundaryTests
{
    [Theory]
    [InlineData("team.red", true)]
    [InlineData("team.red.player-1", true)]
    [InlineData("team.red2.player-1", false)]
    [InlineData("tenant:1", true)]
    [InlineData("tenant:1:admin", true)]
    [InlineData("tenant:10:admin", false)]
    public async Task Target_prefix_grants_require_namespace_boundary(string targetId, bool allowed)
    {
        var messages = new InMemoryPluginMessageSink();
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
        });
        var module = await host.ImportJsonAsync(SendModule(targetId));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite(targetPrefixes: ["team.red", "tenant:1"])
            .WithFuel(10_000)
            .Build());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.Equal(allowed, result.Succeeded);
        if (allowed)
        {
            Assert.Equal(targetId, Assert.Single(messages.Messages).TargetId);
        }
        else
        {
            Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
            Assert.Empty(messages.Messages);
        }
    }

    private static string SendModule(string targetId) =>
        $$"""
        {
          "id": "host-message-boundary",
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
                      { "string": "hello" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """;
}
