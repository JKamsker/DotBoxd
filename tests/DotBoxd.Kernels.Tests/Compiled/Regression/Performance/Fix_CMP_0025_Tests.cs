using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class Fix_CMP_0025_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task List_get_i32_loop_returns_same_value_and_charges_read_fuel(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .Build(),
            mode,
            iterations: 5);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(9, ((I32Value)result.Value!).Value);
        Assert.Equal(5, result.ResourceUsage.LoopIterations);
        Assert.True(result.ResourceUsage.FuelUsed >= 5 * SandboxCollectionFuel.Read(3));
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
          "id": "compiled-list-get-i32-loop",
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
                        "right": {
                          "call": "list.get",
                          "args": [
                            { "var": "items" },
                            { "op": "rem", "left": { "var": "i" }, "right": { "i32": 3 } }
                          ]
                        }
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
