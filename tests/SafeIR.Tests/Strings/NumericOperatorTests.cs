using SafeIR;

namespace SafeIR.Tests;

public sealed class NumericOperatorTests
{
    public static TheoryData<string, string, SandboxValue> NumericCases()
        => new()
        {
            {
                "I64",
                """{ "op": "add", "left": { "i64": 9223372036854775800 }, "right": { "i64": 7 } }""",
                SandboxValue.FromInt64(long.MaxValue)
            },
            {
                "I64",
                """{ "unary": "-", "operand": { "i64": 42 } }""",
                SandboxValue.FromInt64(-42)
            },
            {
                "F64",
                """
                {
                  "op": "div",
                  "left": {
                    "op": "sub",
                    "left": {
                      "op": "mul",
                      "left": { "f64": 5.5 },
                      "right": { "f64": 2.0 }
                    },
                    "right": { "f64": 1.0 }
                  },
                  "right": { "f64": 2.0 }
                }
                """,
                SandboxValue.FromDouble(5.0)
            },
            {
                "F64",
                """{ "unary": "-", "operand": { "f64": 1.25 } }""",
                SandboxValue.FromDouble(-1.25)
            },
            {
                "Bool",
                """{ "op": "gte", "left": { "f64": 2.25 }, "right": { "f64": 2.0 } }""",
                SandboxValue.FromBool(true)
            }
        };

    public static TheoryData<string, string, ExecutionMode> FaultCases()
    {
        var data = new TheoryData<string, string, ExecutionMode>();
        foreach (var mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
        {
            data.Add(
                "I64",
                """{ "op": "add", "left": { "i64": 9223372036854775807 }, "right": { "i64": 1 } }""",
                mode);
            data.Add(
                "I32",
                """{ "op": "rem", "left": { "i32": -2147483648 }, "right": { "i32": -1 } }""",
                mode);
            data.Add(
                "I64",
                """{ "op": "div", "left": { "i64": 1 }, "right": { "i64": 0 } }""",
                mode);
            data.Add(
                "I64",
                """{ "op": "rem", "left": { "i64": 1 }, "right": { "i64": 0 } }""",
                mode);
            data.Add(
                "I64",
                """{ "op": "div", "left": { "i64": -9223372036854775808 }, "right": { "i64": -1 } }""",
                mode);
            data.Add(
                "I64",
                """{ "op": "rem", "left": { "i64": -9223372036854775808 }, "right": { "i64": -1 } }""",
                mode);
            data.Add(
                "F64",
                """{ "op": "mul", "left": { "f64": 1e308 }, "right": { "f64": 1e308 } }""",
                mode);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(NumericCases))]
    public async Task Same_type_numeric_operators_match_between_interpreter_and_compiler(
        string returnType,
        string expression,
        SandboxValue expected)
    {
        var interpreted = await ExecuteReturnAsync(returnType, expression, ExecutionMode.Interpreted);
        var compiled = await ExecuteReturnAsync(returnType, expression, ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(expected, interpreted.Value);
        Assert.Equal(expected, compiled.Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    [Fact]
    public async Task Mixed_numeric_operators_are_rejected_without_widening()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleWithReturn(
            "I64",
            """{ "op": "add", "left": { "i32": 1 }, "right": { "i64": 1 } }"""));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MISMATCH");
    }

    [Theory]
    [MemberData(nameof(FaultCases))]
    public async Task Numeric_faults_are_sandbox_invalid_input_in_both_modes(
        string returnType,
        string expression,
        ExecutionMode mode)
    {
        var result = await ExecuteReturnAsync(returnType, expression, mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteReturnAsync(
        string returnType,
        string expression,
        ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleWithReturn(returnType, expression));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static string ModuleWithReturn(string returnType, string expression)
        => $$"""
        {
          "id": "numeric-operators",
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
