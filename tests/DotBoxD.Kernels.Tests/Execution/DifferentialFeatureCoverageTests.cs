using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Execution;

public sealed class DifferentialFeatureCoverageTests
{
    public static TheoryData<string, SandboxValue, int, int> FeatureCases()
        => new()
        {
            { CallsLoopsAndBooleansJson(), SandboxValue.FromList([SandboxValue.FromInt32(-3), SandboxValue.FromInt32(2)]), 12, 1 },
            { CollectionsStringsAndBindingsJson(), SandboxValue.Unit, 9, 2 }
        };

    [Theory]
    [MemberData(nameof(FeatureCases))]
    public async Task Feature_cases_match_interpreter_and_compiler(
        string json,
        SandboxValue input,
        int expected,
        int expectedHostCalls)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(json);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(20_000).Build());

        var interpreted = await ExecuteAsync(host, plan, input, ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(host, plan, input, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(expected, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(expected, ((I32Value)compiled.Value!).Value);
        Assert.Equal(expectedHostCalls, interpreted.ResourceUsage.HostCalls);
        Assert.Equal(expectedHostCalls, compiled.ResourceUsage.HostCalls);
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input,
        ExecutionMode mode)
        => host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

    private static string CallsLoopsAndBooleansJson()
        => """
        {
          "id": "differential-calls-loops-booleans",
          "version": "1.0.0",
          "functions": [
            {
              "id": "doubleAbs",
              "parameters": [{ "name": "x", "type": "I32" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "op": "mul",
                    "left": { "call": "math.abs", "args": [{ "var": "x" }] },
                    "right": { "i32": 2 }
                  }
                }
              ]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "a", "type": "I32" },
                { "name": "b", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 4 },
                  "body": [
                    {
                      "op": "set",
                      "name": "total",
                      "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } }
                    }
                  ]
                },
                {
                  "op": "if",
                  "condition": {
                    "op": "and",
                    "left": { "op": "lt", "left": { "var": "a" }, "right": { "var": "b" } },
                    "right": { "bool": true }
                  },
                  "then": [
                    {
                      "op": "return",
                      "value": {
                        "op": "add",
                        "left": { "call": "doubleAbs", "args": [{ "var": "a" }] },
                        "right": { "var": "total" }
                      }
                    }
                  ],
                  "else": [{ "op": "return", "value": { "i32": 0 } }]
                }
              ]
            }
          ]
        }
        """;

    private static string CollectionsStringsAndBindingsJson()
        => """
        {
          "id": "differential-collections-strings-bindings",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "items",
                  "value": {
                    "call": "list.add",
                    "args": [
                      { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }] },
                      { "i32": 3 }
                    ]
                  }
                },
                {
                  "op": "set",
                  "name": "labels",
                  "value": {
                    "call": "map.set",
                    "args": [
                      {
                        "call": "map.empty",
                        "genericType": { "name": "Map", "arguments": ["String", "I32"] },
                        "args": []
                      },
                      { "string": "count" },
                      { "call": "list.count", "args": [{ "var": "items" }] }
                    ]
                  }
                },
                {
                  "op": "set",
                  "name": "text",
                  "value": {
                    "call": "string.concatBudgeted",
                    "args": [{ "string": "safe" }, { "string": "ir" }]
                  }
                },
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": { "call": "map.get", "args": [{ "var": "labels" }, { "string": "count" }] },
                    "right": { "call": "string.length", "args": [{ "var": "text" }] }
                  }
                }
              ]
            }
          ]
        }
        """;
}
