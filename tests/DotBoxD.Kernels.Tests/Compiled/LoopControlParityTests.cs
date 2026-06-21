using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled;

/// <summary>
/// Issue #68: <c>continue</c>/<c>break</c> are first-class kernel IR loop-control statements.
/// These tests pin the runtime semantics on both the interpreter and the compiled backend
/// (the two must agree), covering the innermost-loop targeting required when loop control is
/// nested inside <c>if</c>s and inside multiple loop levels, for both <c>forRange</c> and
/// <c>while</c>. A top-level <c>break</c> is rejected by validation.
/// </summary>
public sealed class LoopControlParityTests
{
    [Fact]
    public Task Continue_skips_iterations()
        // [2, -5, 0, 4]: skip v <= 0  ->  2 + 4
        => AssertParityAsync(SumPositivesForeachJson(), IntList(2, -5, 0, 4), expected: 6);

    [Fact]
    public Task Break_exits_loop()
        // [3, 4, -1, 9]: stop at the first v < 0  ->  3 + 4
        => AssertParityAsync(SumUntilNegativeForeachJson(), IntList(3, 4, -1, 9), expected: 7);

    [Fact]
    public Task Continue_and_break_target_the_innermost_loop()
        // For each v, an inner range loop skips j == 1 and breaks when v < 0; the outer loop keeps going.
        // [2, -3, 5]: (2 + 2) + (break, adds nothing) + (5 + 5) = 14
        => AssertParityAsync(NestedLoopJson(), IntList(2, -3, 5), expected: 14);

    [Fact]
    public Task Continue_inside_nested_ifs_targets_the_loop()
        // Skip the sentinel 100 from inside two nested ifs. [5, 100, 7] -> 5 + 7
        => AssertParityAsync(ContinueInsideNestedIfsJson(), IntList(5, 100, 7), expected: 12);

    [Fact]
    public Task While_loop_supports_continue_and_break()
        // i is advanced before continue; skip negatives, break above 100. [5, -3, 7, 200, 9] -> 5 + 7
        => AssertParityAsync(WhileLoopControlJson(), IntList(5, -3, 7, 200, 9), expected: 12);

    [Fact]
    public Task Both_if_branches_jumping_out_compiles_to_valid_il()
        // Accumulate, then `if (v < 0) break; else continue;` — BOTH branches terminate the block, so
        // EmitIf marks no fall-through label. This is the same IL shape as return-in-both-branches and
        // must still produce valid IL on the compiled backend. [3, 4, -1, 9] -> sums up to and including
        // the first negative: 3 + 4 + (-1) = 6, then breaks.
        => AssertParityAsync(SumThroughFirstNegativeJson(), IntList(3, 4, -1, 9), expected: 6);

    [Fact]
    public async Task Break_outside_a_loop_is_rejected_by_validation()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(TopLevelBreakJson());

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build()));

        Assert.Contains(exception.Diagnostics, d => d.Code == "E-LOOP-CONTROL");
    }

    private static async Task AssertParityAsync(string moduleJson, SandboxValue input, int expected)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(10_000).Build());

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, interpreted.ActualMode);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(expected, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(expected, ((I32Value)compiled.Value!).Value);
    }

    private static SandboxValue IntList(params int[] values)
        => SandboxValue.FromList(values.Select(SandboxValue.FromInt32).ToArray(), SandboxType.I32);

    private static string SumPositivesForeachJson()
        => ListParamModule("""
            { "op": "set", "name": "total", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "call": "list.count", "args": [{ "var": "values" }] },
              "body": [
                { "op": "set", "name": "v", "value": { "call": "list.get", "args": [{ "var": "values" }, { "var": "i" }] } },
                {
                  "op": "if",
                  "condition": { "op": "lte", "left": { "var": "v" }, "right": { "i32": 0 } },
                  "then": [{ "op": "continue" }],
                  "else": []
                },
                { "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "v" } } }
              ]
            },
            { "op": "return", "value": { "var": "total" } }
            """);

    private static string SumUntilNegativeForeachJson()
        => ListParamModule("""
            { "op": "set", "name": "total", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "call": "list.count", "args": [{ "var": "values" }] },
              "body": [
                { "op": "set", "name": "v", "value": { "call": "list.get", "args": [{ "var": "values" }, { "var": "i" }] } },
                {
                  "op": "if",
                  "condition": { "op": "lt", "left": { "var": "v" }, "right": { "i32": 0 } },
                  "then": [{ "op": "break" }],
                  "else": []
                },
                { "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "v" } } }
              ]
            },
            { "op": "return", "value": { "var": "total" } }
            """);

    private static string NestedLoopJson()
        => ListParamModule("""
            { "op": "set", "name": "total", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "call": "list.count", "args": [{ "var": "values" }] },
              "body": [
                { "op": "set", "name": "v", "value": { "call": "list.get", "args": [{ "var": "values" }, { "var": "i" }] } },
                {
                  "op": "forRange",
                  "local": "j",
                  "start": { "i32": 0 },
                  "end": { "i32": 3 },
                  "body": [
                    {
                      "op": "if",
                      "condition": { "op": "eq", "left": { "var": "j" }, "right": { "i32": 1 } },
                      "then": [{ "op": "continue" }],
                      "else": []
                    },
                    {
                      "op": "if",
                      "condition": { "op": "lt", "left": { "var": "v" }, "right": { "i32": 0 } },
                      "then": [{ "op": "break" }],
                      "else": []
                    },
                    { "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "v" } } }
                  ]
                }
              ]
            },
            { "op": "return", "value": { "var": "total" } }
            """);

    private static string ContinueInsideNestedIfsJson()
        => ListParamModule("""
            { "op": "set", "name": "total", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "call": "list.count", "args": [{ "var": "values" }] },
              "body": [
                { "op": "set", "name": "v", "value": { "call": "list.get", "args": [{ "var": "values" }, { "var": "i" }] } },
                {
                  "op": "if",
                  "condition": { "op": "gt", "left": { "var": "v" }, "right": { "i32": 0 } },
                  "then": [
                    {
                      "op": "if",
                      "condition": { "op": "eq", "left": { "var": "v" }, "right": { "i32": 100 } },
                      "then": [{ "op": "continue" }],
                      "else": []
                    },
                    { "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "v" } } }
                  ],
                  "else": []
                }
              ]
            },
            { "op": "return", "value": { "var": "total" } }
            """);

    private static string WhileLoopControlJson()
        => ListParamModule("""
            { "op": "set", "name": "total", "value": { "i32": 0 } },
            { "op": "set", "name": "i", "value": { "i32": 0 } },
            {
              "op": "while",
              "condition": { "op": "lt", "left": { "var": "i" }, "right": { "call": "list.count", "args": [{ "var": "values" }] } },
              "body": [
                { "op": "set", "name": "v", "value": { "call": "list.get", "args": [{ "var": "values" }, { "var": "i" }] } },
                { "op": "set", "name": "i", "value": { "op": "add", "left": { "var": "i" }, "right": { "i32": 1 } } },
                {
                  "op": "if",
                  "condition": { "op": "lt", "left": { "var": "v" }, "right": { "i32": 0 } },
                  "then": [{ "op": "continue" }],
                  "else": []
                },
                {
                  "op": "if",
                  "condition": { "op": "gt", "left": { "var": "v" }, "right": { "i32": 100 } },
                  "then": [{ "op": "break" }],
                  "else": []
                },
                { "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "v" } } }
              ]
            },
            { "op": "return", "value": { "var": "total" } }
            """);

    private static string SumThroughFirstNegativeJson()
        => ListParamModule("""
            { "op": "set", "name": "total", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "call": "list.count", "args": [{ "var": "values" }] },
              "body": [
                { "op": "set", "name": "v", "value": { "call": "list.get", "args": [{ "var": "values" }, { "var": "i" }] } },
                { "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "var": "v" } } },
                {
                  "op": "if",
                  "condition": { "op": "lt", "left": { "var": "v" }, "right": { "i32": 0 } },
                  "then": [{ "op": "break" }],
                  "else": [{ "op": "continue" }]
                }
              ]
            },
            { "op": "return", "value": { "var": "total" } }
            """);

    private static string TopLevelBreakJson()
        => $$"""
        {
          "id": "loop-control-top-level-break",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "break" },
                { "op": "return", "value": { "i32": 0 } }
              ]
            }
          ]
        }
        """;

    private static string ListParamModule(string body)
        => $$"""
        {
          "id": "loop-control-parity",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "values", "type": { "name": "List", "arguments": ["I32"] } }
              ],
              "returnType": "I32",
              "body": [
                {{body}}
              ]
            }
          ]
        }
        """;
}
