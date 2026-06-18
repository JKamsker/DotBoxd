using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class Fix_CMP_0034_Tests
{
    [Theory]
    [InlineData("abs", ExecutionMode.Interpreted)]
    [InlineData("abs", ExecutionMode.Compiled)]
    [InlineData("min", ExecutionMode.Interpreted)]
    [InlineData("min", ExecutionMode.Compiled)]
    [InlineData("max", ExecutionMode.Interpreted)]
    [InlineData("max", ExecutionMode.Compiled)]
    [InlineData("clamp", ExecutionMode.Interpreted)]
    [InlineData("clamp", ExecutionMode.Compiled)]
    public async Task I32_math_intrinsic_loop_returns_same_value_and_charges_bindings(
        string intrinsic,
        ExecutionMode mode)
    {
        const int iterations = 100;
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleJson(intrinsic));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(20_000)
                .WithMaxHostCalls(1_000)
                .WithMaxLoopIterations(1_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(Expected(intrinsic, iterations), ((I32Value)result.Value!).Value);
        Assert.Equal(iterations, result.ResourceUsage.HostCalls);
        Assert.Equal(mode, result.ActualMode);
    }

    private static int Expected(string intrinsic, int iterations)
    {
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total = (total + IntrinsicValue(intrinsic, i)) % 1_000_003;
        }

        return total;
    }

    private static int IntrinsicValue(string intrinsic, int i)
        => intrinsic switch
        {
            "abs" => Math.Abs(i - 50),
            "min" => Math.Min(i, 17),
            "max" => Math.Max(i, 3),
            _ => Math.Clamp(i, 0, 20)
        };

    private static string ModuleJson(string intrinsic)
        => $$"""
        {
          "id": "compiled-i32-math-{{intrinsic}}-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
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
                        "op": "rem",
                        "left": {
                          "op": "add",
                          "left": { "var": "total" },
                          "right": {{IntrinsicJson(intrinsic)}}
                        },
                        "right": { "i32": 1000003 }
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

    private static string IntrinsicJson(string intrinsic)
        => intrinsic switch
        {
            "abs" => """
            {
              "call": "math.abs",
              "args": [{ "op": "sub", "left": { "var": "i" }, "right": { "i32": 50 } }]
            }
            """,
            "min" => """
            {
              "call": "math.min",
              "args": [{ "var": "i" }, { "i32": 17 }]
            }
            """,
            "max" => """
            {
              "call": "math.max",
              "args": [{ "var": "i" }, { "i32": 3 }]
            }
            """,
            _ => """
            {
              "call": "math.clamp",
              "args": [{ "var": "i" }, { "i32": 0 }, { "i32": 20 }]
            }
            """
        };
}
