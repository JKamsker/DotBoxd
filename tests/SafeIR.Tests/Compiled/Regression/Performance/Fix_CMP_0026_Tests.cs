using SafeIR;

namespace SafeIR.Tests;

public sealed class Fix_CMP_0026_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Map_get_i32_loop_returns_same_value_and_charges_read_fuel(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SandboxPolicyBuilder.Create()
                .WithFuel(1_000)
                .WithMaxLoopIterations(10)
                .Build(),
            mode,
            iterations: 4);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(28, ((I32Value)result.Value!).Value);
        Assert.True(result.ResourceUsage.FuelUsed >= 4 * SandboxCollectionFuel.Read(1));
        Assert.True(result.ResourceUsage.StringBytes >= 4 * "alice".Length * sizeof(char));
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
          "id": "compiled-map-get-i32-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "scores",
                  "value": {
                    "call": "map.set",
                    "args": [
                      { "call": "map.empty", "genericType": { "name": "Map", "arguments": ["String", "I32"] }, "args": [] },
                      { "string": "alice" },
                      { "i32": 7 }
                    ]
                  }
                },
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
                          "call": "map.get",
                          "args": [{ "var": "scores" }, { "string": "alice" }]
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
