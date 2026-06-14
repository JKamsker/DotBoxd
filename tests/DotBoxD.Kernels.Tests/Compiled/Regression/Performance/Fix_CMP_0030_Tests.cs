using DotBoxD.Kernels;

namespace DotBoxD.Kernels.Tests;

public sealed class Fix_CMP_0030_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Modulo_index_while_accumulator_matches_expected_result(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            WhileAccumulatorModuleJson(initialTotal: 0, divisor: 1_000_003),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .Build(),
            mode,
            iterations: 8);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(28, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Modulo_index_while_accumulator_falls_back_for_negative_current_value(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            WhileAccumulatorModuleJson(initialTotal: -1, divisor: 3),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .Build(),
            mode,
            iterations: 2);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(0, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        string moduleJson,
        SandboxPolicy policy,
        ExecutionMode mode,
        int iterations)
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

    private static string WhileAccumulatorModuleJson(int initialTotal, int divisor)
        => $$"""
        {
          "id": "modulo-index-while-accumulator",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "i", "value": { "i32": 0 } },
                { "op": "set", "name": "total", "value": { "i32": {{initialTotal}} } },
                {
                  "op": "while",
                  "condition": { "op": "lt", "left": { "var": "i" }, "right": { "var": "iterations" } },
                  "body": [
                    { "op": "set", "name": "total", "value": {
                      "op": "rem",
                      "left": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } },
                      "right": { "i32": {{divisor}} } } },
                    { "op": "set", "name": "i", "value": {
                      "op": "add",
                      "left": { "var": "i" },
                      "right": { "i32": 1 } } }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
