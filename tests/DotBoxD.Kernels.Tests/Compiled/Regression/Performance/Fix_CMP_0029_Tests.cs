using DotBoxD.Kernels;

namespace DotBoxD.Kernels.Tests;

public sealed class Fix_CMP_0029_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Modulo_branch_accumulator_matches_expected_result(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            BranchAccumulatorModuleJson(),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .Build(),
            mode,
            iterations: 5);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Modulo_branch_accumulator_rejects_opposite_sign_deltas(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            OppositeSignDeltaModuleJson(),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .Build(),
            mode,
            iterations: 4);

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

    private static string BranchAccumulatorModuleJson()
        => """
        {
          "id": "modulo-branch-accumulator",
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
                      "op": "if",
                      "condition": {
                        "op": "eq",
                        "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } },
                        "right": { "i32": 0 }
                      },
                      "then": [
                        { "op": "set", "name": "total", "value": {
                          "op": "add",
                          "left": { "var": "total" },
                          "right": { "i32": 1 } } }
                      ],
                      "else": [
                        { "op": "set", "name": "total", "value": {
                          "op": "add",
                          "left": { "var": "total" },
                          "right": { "i32": 2 } } }
                      ]
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string OppositeSignDeltaModuleJson()
        => """
        {
          "id": "modulo-branch-opposite-sign-deltas",
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
                      "op": "if",
                      "condition": {
                        "op": "eq",
                        "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } },
                        "right": { "i32": 0 }
                      },
                      "then": [
                        { "op": "set", "name": "total", "value": {
                          "op": "add",
                          "left": { "var": "total" },
                          "right": { "i32": 2147483647 } } }
                      ],
                      "else": [
                        { "op": "set", "name": "total", "value": {
                          "op": "add",
                          "left": { "var": "total" },
                          "right": { "i32": -2147483647 } } }
                      ]
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
