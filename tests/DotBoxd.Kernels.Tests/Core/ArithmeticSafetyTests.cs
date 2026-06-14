using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class ArithmeticSafetyTests
{
    public static TheoryData<ExecutionMode, string> FaultCases()
    {
        var cases = new[] {
            """{ "op": "add", "left": { "i32": 2147483647 }, "right": { "i32": 1 } }""",
            """{ "op": "sub", "left": { "i32": -2147483648 }, "right": { "i32": 1 } }""",
            """{ "op": "mul", "left": { "i32": 1073741824 }, "right": { "i32": 2 } }""",
            """{ "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } }""",
            """{ "op": "rem", "left": { "i32": 1 }, "right": { "i32": 0 } }""",
            """{ "op": "div", "left": { "i32": -2147483648 }, "right": { "i32": -1 } }""",
            """{ "unary": "-", "operand": { "i32": -2147483648 } }"""
        };
        var data = new TheoryData<ExecutionMode, string>();
        foreach (var expression in cases) {
            data.Add(ExecutionMode.Interpreted, expression);
            data.Add(ExecutionMode.Compiled, expression);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FaultCases))]
    public async Task I32_arithmetic_faults_are_sandbox_invalid_input(
        ExecutionMode mode,
        string expression)
    {
        var result = await ExecuteReturnAsync(expression, mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async ValueTask<SandboxExecutionResult> ExecuteReturnAsync(
        string expression,
        ExecutionMode mode)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "arithmetic-safety",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }
}
