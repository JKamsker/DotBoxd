using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class FailureAuditParityTestSupport
{
    public static SandboxHost BuildHost(InMemoryPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static async ValueTask<ExecutionPlan> PrepareMessagePlanAsync(
        SandboxHost host,
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

    public static string MessageModuleJson(string targetId, string message)
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

    public static string TwoSendModuleJson()
        => """
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

    public static void AssertFailedRunSummary(
        SandboxExecutionResult result,
        SandboxErrorCode expectedCode)
    {
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.False(summary.Success);
        Assert.Equal(expectedCode, summary.ErrorCode);
    }
}
