using SafeIR;

namespace SafeIR.Tests;

public sealed class Fix_CMP_0027_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Nested_F64_math_binding_loop_charges_each_crossing(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .WithMaxHostCalls(20)
                .Build(),
            mode,
            iterations: 4);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(1.0, ((F64Value)result.Value!).Value);
        Assert.Equal(12, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Nested_F64_math_binding_loop_falls_back_for_scaled_host_call_quota(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .WithMaxHostCalls(11)
                .Build(),
            mode,
            iterations: 4);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(12, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        SandboxPolicy policy,
        ExecutionMode mode,
        int iterations)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static string ModuleJson()
        => """
        {
          "id": "nested-f64-binding-loop",
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
                      "value": {
                        "call": "math.sqrt",
                        "args": [{
                          "call": "math.sqrt",
                          "args": [{
                            "call": "math.sqrt",
                            "args": [{ "var": "total" }]
                          }]
                        }]
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
