using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.SideEffectParity;

internal static class TimeParityTestSupport
{
    public static SandboxHost BuildHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddTimeBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });

    public static async Task<SandboxExecutionResult> RunAsync(
        string moduleId,
        DateTimeOffset logicalNow,
        ExecutionMode mode)
    {
        using var host = BuildHost();
        var module = await host.ImportJsonAsync(ModuleJson(moduleId));
        var policy = SandboxPolicyBuilder.Create()
            .GrantTimeNow()
            .Deterministic(logicalNow, randomSeed: 0)
            .WithFuel(10_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    public static async Task<SandboxExecutionResult> NondeterministicRunAsync(
        string moduleId,
        ExecutionMode mode)
    {
        using var host = BuildHost();
        var module = await host.ImportJsonAsync(ModuleJson(moduleId));
        var policy = SandboxPolicyBuilder.Create()
            .GrantTimeNow()
            .WithFuel(10_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    public static async Task<SandboxExecutionResult> DoubleCallRunAsync(
        string moduleId,
        DateTimeOffset logicalNow,
        ExecutionMode mode)
    {
        using var host = BuildHost();
        var module = await host.ImportJsonAsync(DoubleCallModuleJson(moduleId));
        var policy = SandboxPolicyBuilder.Create()
            .GrantTimeNow()
            .Deterministic(logicalNow, randomSeed: 0)
            .WithFuel(10_000)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    public static string ModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "time.now" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I64",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "time.nowUnixMillis", "args": [] }
                }
              ]
            }
          ]
        }
        """;

    public static string DoubleCallModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "time.now" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I64",
              "body": [
                {
                  "op": "set",
                  "name": "first",
                  "value": { "call": "time.nowUnixMillis", "args": [] }
                },
                {
                  "op": "return",
                  "value": { "call": "time.nowUnixMillis", "args": [] }
                }
              ]
            }
          ]
        }
        """;
}
