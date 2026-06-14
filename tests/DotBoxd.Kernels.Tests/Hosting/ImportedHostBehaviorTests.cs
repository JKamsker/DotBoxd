namespace DotBoxd.Kernels.Tests;

public sealed class ImportedHostBehaviorTests
{
    public static TheoryData<ExecutionMode> Modes()
        => new() {
            ExecutionMode.Interpreted,
            ExecutionMode.Compiled
        };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Parameter_count_mismatch_returns_invalid_input(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            TwoParameterModule(),
            SandboxValue.FromInt32(1),
            mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Parameter_type_mismatch_returns_invalid_input(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            SingleI32ParameterModule(),
            SandboxValue.FromDouble(3.14),
            mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Division_by_zero_returns_invalid_input(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            ReturnExpressionModule(
                """{ "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } }""",
                "I32"),
            SandboxValue.Unit,
            mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Conditional_selects_branch_by_parameter(ExecutionMode mode)
    {
        var hit = await ExecuteAsync(ConditionalModule(), SandboxValue.FromInt32(5), mode);
        var miss = await ExecuteAsync(ConditionalModule(), SandboxValue.FromInt32(-5), mode);

        Assert.True(hit.Succeeded, hit.Error?.SafeMessage);
        Assert.True(miss.Succeeded, miss.Error?.SafeMessage);
        Assert.Equal(1, ((I32Value)hit.Value!).Value);
        Assert.Equal(-1, ((I32Value)miss.Value!).Value);
        Assert.Equal(mode, hit.ActualMode);
        Assert.Equal(mode, miss.ActualMode);
    }

    [Fact]
    public async Task Compiled_for_range_loop_matches_interpreter()
    {
        var interpreted = await ExecuteAsync(SumForRangeModule(), SandboxValue.FromInt32(11), ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(SumForRangeModule(), SandboxValue.FromInt32(11), ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(55, ((I32Value)interpreted.Value!).Value);
        Assert.Equal(((I32Value)interpreted.Value).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Prepared_plan_can_be_executed_with_different_inputs(ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(SumForRangeModule());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var options = new SandboxExecutionOptions {
            Mode = mode,
            AllowFallbackToInterpreter = false
        };

        var first = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(6), options);
        var second = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(11), options);

        Assert.True(first.Succeeded, first.Error?.SafeMessage);
        Assert.True(second.Succeeded, second.Error?.SafeMessage);
        Assert.Equal(15, ((I32Value)first.Value!).Value);
        Assert.Equal(55, ((I32Value)second.Value!).Value);
        Assert.Equal(mode, first.ActualMode);
        Assert.Equal(mode, second.ActualMode);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Nested_private_function_calls_execute(ExecutionMode mode)
    {
        var result = await ExecuteAsync(NestedFunctionModule(), SandboxValue.FromInt32(5), mode);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(12, ((I32Value)result.Value!).Value);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        string json,
        SandboxValue input,
        ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(json);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions {
                Mode = mode,
                AllowFallbackToInterpreter = false
            });
    }

    private static string SingleI32ParameterModule()
        => """
        {
          "id": "single-input",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "var": "value" } }]
            }
          ]
        }
        """;

    private static string TwoParameterModule()
        => """
        {
          "id": "two-inputs",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "left", "type": "I32" },
                { "name": "right", "type": "I32" }
              ],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "op": "add", "left": { "var": "left" }, "right": { "var": "right" } }
                }
              ]
            }
          ]
        }
        """;

    private static string ConditionalModule()
        => """
        {
          "id": "conditional",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "if",
                  "condition": { "op": "gt", "left": { "var": "value" }, "right": { "i32": 0 } },
                  "then": [{ "op": "return", "value": { "i32": 1 } }],
                  "else": [{ "op": "return", "value": { "i32": -1 } }]
                }
              ]
            }
          ]
        }
        """;

    private static string SumForRangeModule()
        => """
        {
          "id": "compiled-sum-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "exclusiveEnd", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "sum", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 1 },
                  "end": { "var": "exclusiveEnd" },
                  "body": [
                    {
                      "op": "set",
                      "name": "sum",
                      "value": { "op": "add", "left": { "var": "sum" }, "right": { "var": "i" } }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "sum" } }
              ]
            }
          ]
        }
        """;

    private static string NestedFunctionModule()
        => """
        {
          "id": "nested-private-functions",
          "version": "1.0.0",
          "functions": [
            {
              "id": "inc",
              "parameters": [{ "name": "x", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "op": "add", "left": { "var": "x" }, "right": { "i32": 1 } } }]
            },
            {
              "id": "double",
              "parameters": [{ "name": "x", "type": "I32" }],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "op": "mul", "left": { "var": "x" }, "right": { "i32": 2 } } }]
            },
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "value", "type": "I32" }],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "double", "args": [{ "call": "inc", "args": [{ "var": "value" }] }] }
                }
              ]
            }
          ]
        }
        """;

    private static string ReturnExpressionModule(string expression, string returnType)
        => $$"""
        {
          "id": "host-behavior",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;
}
