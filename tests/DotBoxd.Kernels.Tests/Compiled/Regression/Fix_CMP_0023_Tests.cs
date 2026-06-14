using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class Fix_CMP_0023_Tests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task I32_loop_local_function_call_matches_expected_result(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            LocalCallJson(),
            SandboxPolicyBuilder.Create().WithFuel(10_000).WithMaxLoopIterations(100).Build(),
            mode,
            SandboxValue.FromInt32(8));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(8, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task I32_loop_local_function_call_enforces_call_depth(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            LocalCallJson(),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .WithMaxCallDepth(1)
                .Build(),
            mode,
            SandboxValue.FromInt32(1));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Nested_local_call_argument_preserves_call_depth_order(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            NestedArgumentJson(),
            SandboxPolicyBuilder.Create()
                .WithFuel(10_000)
                .WithMaxLoopIterations(100)
                .WithMaxCallDepth(2)
                .Build(),
            mode,
            SandboxValue.FromInt32(1));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(2, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        string moduleJson,
        SandboxPolicy policy,
        ExecutionMode mode,
        SandboxValue input)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static string LocalCallJson()
        => """
        {
          "id": "compiled-local-function-fast-path",
          "version": "1.0.0",
          "functions": [
            {
              "id": "increment",
              "visibility": "private",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": { "var": "value" },
                    "right": { "i32": 1 }
                  }
                }
              ]
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
                    {
                      "op": "set",
                      "name": "total",
                      "value": { "call": "increment", "args": [{ "var": "total" }] }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;

    private static string NestedArgumentJson()
        => """
        {
          "id": "compiled-local-function-nested-argument",
          "version": "1.0.0",
          "functions": [
            {
              "id": "increment",
              "visibility": "private",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": { "var": "value" },
                    "right": { "i32": 1 }
                  }
                }
              ]
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
                    {
                      "op": "set",
                      "name": "total",
                      "value": {
                        "call": "increment",
                        "args": [
                          { "call": "increment", "args": [{ "var": "total" }] }
                        ]
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
