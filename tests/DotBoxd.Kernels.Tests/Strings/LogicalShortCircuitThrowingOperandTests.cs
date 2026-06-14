using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

public sealed partial class LogicalShortCircuitTests
{
    [Theory]
    [MemberData(nameof(Modes))]
    public async Task And_short_circuits_before_throwing_pure_right_operand(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleWithHelperJson(
            HelperReturns(false),
            """
            {
              "op": "and",
              "left": { "call": "helper", "args": [] },
              "right": {
                "op": "eq",
                "left": { "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } },
                "right": { "i32": 0 }
              }
            }
            """));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.False(((BoolValue)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Or_short_circuits_before_throwing_pure_right_operand(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleWithHelperJson(
            HelperReturns(true),
            """
            {
              "op": "or",
              "left": { "call": "helper", "args": [] },
              "right": {
                "op": "eq",
                "left": { "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } },
                "right": { "i32": 0 }
              }
            }
            """));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.True(((BoolValue)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    private static string ModuleWithHelperJson(string helperReturn, string expression)
        => $$"""
        {
          "id": "logical-short-circuit",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Bool",
              "body": [{ "op": "return", "value": {{expression}} }]
            },
            {
              "id": "helper",
              "visibility": "private",
              "parameters": [],
              "returnType": "Bool",
              "body": [
                { "op": "set", "name": "a", "value": { "op": "add", "left": { "i32": 1 }, "right": { "i32": 1 } } },
                { "op": "set", "name": "b", "value": { "op": "add", "left": { "var": "a" }, "right": { "i32": 1 } } },
                { "op": "return", "value": {{helperReturn}} }
              ]
            }
          ]
        }
        """;

    private static string HelperReturns(bool value)
        => value ? """{ "bool": true }""" : """{ "bool": false }""";
}
