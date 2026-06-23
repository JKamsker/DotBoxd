using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class QuotaParityTestSupport
{
    public static SandboxHost QuotaParityPureHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static SandboxHost QuotaParityLogHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static SandboxHost QuotaParityMessageHost(InMemoryPluginMessageSink sink)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(sink);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static SandboxPolicy QuotaParityPurePolicy(int maxHostCalls, long maxFuel)
        => SandboxPolicyBuilder.Create()
            .WithFuel(maxFuel)
            .WithMaxHostCalls(maxHostCalls)
            .Build();

    public static SandboxPolicy QuotaParityMessagePolicy(int maxHostCalls, long maxFuel)
        => SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(maxFuel)
            .WithMaxHostCalls(maxHostCalls)
            .Build();

    public static ValueTask<SandboxExecutionResult> RunAsync(
        SandboxHost host,
        ExecutionPlan plan,
        ExecutionMode mode)
        => host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    public static string QuotaParitySingleAbsJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "math.abs", "args": [{ "i32": -7 }] }
                }
              ]
            }
          ]
        }
        """;

    public static string QuotaParityDoubleAbsJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "expr", "value": { "call": "math.abs", "args": [{ "i32": -1 }] } },
                { "op": "return", "value": { "call": "math.abs", "args": [{ "i32": -2 }] } }
              ]
            }
          ]
        }
        """;

    public static string QuotaParityPureComputeJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
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
                    "op": "add",
                    "left": { "op": "mul", "left": { "i32": 3 }, "right": { "i32": 4 } },
                    "right": { "i32": 5 }
                  }
                }
              ]
            }
          ]
        }
        """;

    public static string QuotaParityLogJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "quota test" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "hello" }] } }
              ]
            }
          ]
        }
        """;

    public static string QuotaParityDoubleLogJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "quota test" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "first" }] } },
                { "op": "return", "value": { "call": "log.warn", "args": [{ "string": "second" }] } }
              ]
            }
          ]
        }
        """;

    public static string QuotaParitySingleSendJson(string id)
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
                    "args": [{ "string": "player-1" }, { "string": "hello" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    public static string QuotaParityDoubleSendJson(string id)
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
                  "op": "expr",
                  "value": {
                    "call": "host.message.send",
                    "args": [{ "string": "player-1" }, { "string": "first" }]
                  }
                },
                {
                  "op": "return",
                  "value": {
                    "call": "host.message.send",
                    "args": [{ "string": "player-2" }, { "string": "second" }]
                  }
                }
              ]
            }
          ]
        }
        """;
}
