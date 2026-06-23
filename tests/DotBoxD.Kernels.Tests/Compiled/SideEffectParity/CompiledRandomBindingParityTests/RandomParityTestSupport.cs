using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class RandomParityTestSupport
{
    public static SandboxHost CreateHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddRandomBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static string SingleCallModuleJson(string id)
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

    public static string TwoCallModuleJson(string id)
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

    public static SandboxPolicy DeterministicPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantRandom()
            .Deterministic(DateTimeOffset.UnixEpoch, randomSeed: 42UL)
            .WithFuel(10_000)
            .Build();

    public static async Task<SandboxExecutionResult> ExecuteAsync(
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
}
