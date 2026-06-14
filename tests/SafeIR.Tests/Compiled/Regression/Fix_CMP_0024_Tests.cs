using SafeIR;

namespace SafeIR.Tests;

public sealed class Fix_CMP_0024_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task F64_math_binding_loop_returns_same_value(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .WithMaxHostCalls(10)
                .Build(),
            mode,
            iterations: 4,
            F64ModuleJson());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1.0, ((F64Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task F64_math_binding_loop_falls_back_for_host_call_quota(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .WithMaxHostCalls(1)
                .Build(),
            mode,
            iterations: 2,
            F64ModuleJson());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(2, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task String_length_binding_loop_returns_same_value(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .WithMaxHostCalls(10)
                .Build(),
            mode,
            iterations: 4,
            StringLengthModuleJson());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(24, ((I32Value)result.Value!).Value);
        Assert.Equal(4, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task String_length_binding_loop_falls_back_for_host_call_quota(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .WithMaxHostCalls(1)
                .Build(),
            mode,
            iterations: 2,
            StringLengthModuleJson());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(2, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task List_count_loop_returns_same_value_and_charges_read_fuel(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .Build(),
            mode,
            iterations: 4,
            ListCountModuleJson());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(12, ((I32Value)result.Value!).Value);
        Assert.True(result.ResourceUsage.FuelUsed >= 4 * SandboxCollectionFuel.Read(3));
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxPolicy policy,
        ExecutionMode mode,
        int iterations,
        string moduleJson)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static string F64ModuleJson()
        => """
        {
          "id": "compiled-f64-binding-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "F64",
              "body": [
                { "op": "set", "name": "total", "value": { "f64": 1.0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "iterations" },
                  "body": [
                    {
                      "op": "set",
                      "name": "total",
                      "value": { "call": "math.sqrt", "args": [{ "var": "total" }] }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string StringLengthModuleJson()
        => """
        {
          "id": "compiled-string-length-binding-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "text", "value": { "string": "abcdef" } },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "iterations" },
                  "body": [
                    {
                      "op": "set",
                      "name": "total",
                      "value": {
                        "op": "add",
                        "left": { "var": "total" },
                        "right": { "call": "string.length", "args": [{ "var": "text" }] }
                      }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string ListCountModuleJson()
        => """
        {
          "id": "compiled-list-count-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "items", "value": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }] } },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "iterations" },
                  "body": [
                    {
                      "op": "set",
                      "name": "total",
                      "value": {
                        "op": "add",
                        "left": { "var": "total" },
                        "right": { "call": "list.count", "args": [{ "var": "items" }] }
                      }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
