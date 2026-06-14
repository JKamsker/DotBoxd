using DotBoxD.Kernels;

namespace DotBoxD.Kernels.Tests;

public sealed class Fix_CMP_0028_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Two_arg_local_function_modulo_accumulator_matches_expected_result(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            TwoArgAddModuleJson(),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .Build(),
            mode,
            iterations: 8);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Two_arg_local_function_modulo_accumulator_enforces_call_depth(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            TwoArgAddModuleJson(),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .WithMaxCallDepth(1)
                .Build(),
            mode,
            iterations: 1);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Two_arg_local_function_requires_both_parameters_in_add_body(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            DuplicateLeftModuleJson(),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .Build(),
            mode,
            iterations: 3);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(8, ((I32Value)result.Value!).Value);
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

    private static string TwoArgAddModuleJson()
        => """
        {
          "id": "two-arg-local-function-modulo-accumulator",
          "version": "1.0.0",
          "functions": [
            {
              "id": "add",
              "visibility": "private",
              "parameters": [
                { "name": "left", "type": "I32" },
                { "name": "right", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [{ "op": "return", "value": {
                "op": "add",
                "left": { "var": "left" },
                "right": { "var": "right" } } }]
            },
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
                    { "op": "set", "name": "total", "value": {
                      "call": "add",
                      "args": [
                        { "var": "total" },
                        { "op": "rem", "left": { "var": "i" }, "right": { "i32": 3 } }
                      ] } }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string DuplicateLeftModuleJson()
        => """
        {
          "id": "two-arg-local-function-duplicate-left",
          "version": "1.0.0",
          "functions": [
            {
              "id": "doubleLeft",
              "visibility": "private",
              "parameters": [
                { "name": "left", "type": "I32" },
                { "name": "right", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [{ "op": "return", "value": {
                "op": "add",
                "left": { "var": "left" },
                "right": { "var": "left" } } }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 1 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "iterations" },
                  "body": [
                    { "op": "set", "name": "total", "value": {
                      "call": "doubleLeft",
                      "args": [
                        { "var": "total" },
                        { "op": "rem", "left": { "var": "i" }, "right": { "i32": 3 } }
                      ] } }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
