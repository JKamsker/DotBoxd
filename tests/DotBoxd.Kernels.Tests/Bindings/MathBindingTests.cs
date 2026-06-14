using DotBoxd.Kernels;

namespace DotBoxd.Kernels.Tests;

public sealed class MathBindingTests
{
    public static TheoryData<string, double> F64Cases()
        => new() {
            { """{ "call": "math.sqrt", "args": [{ "f64": 9.0 }] }""", 3.0 },
            { """{ "call": "math.floor", "args": [{ "f64": 3.8 }] }""", 3.0 },
            { """{ "call": "math.ceil", "args": [{ "f64": 3.2 }] }""", 4.0 },
            { """{ "call": "math.round", "args": [{ "f64": 2.6 }] }""", 3.0 }
        };

    [Fact]
    public async Task Interpreted_i32_math_bindings_execute()
    {
        var result = await ExecuteReturnAsync(
            """
            {
              "call": "math.clamp",
              "args": [
                {
                  "call": "math.max",
                  "args": [
                    { "call": "math.min", "args": [{ "i32": -3 }, { "i32": 5 }] },
                    { "call": "math.abs", "args": [{ "i32": -7 }] }
                  ]
                },
                { "i32": 0 },
                { "i32": 6 }
              ]
            }
            """,
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(6, ((I32Value)result.Value!).Value);
    }

    [Theory]
    [MemberData(nameof(F64Cases))]
    public async Task Interpreted_f64_math_bindings_execute(string expression, double expected)
    {
        var result = await ExecuteReturnAsync(
            expression,
            "F64",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((F64Value)result.Value!).Value, precision: 10);
    }

    [Fact]
    public async Task Compiled_i32_math_bindings_match_interpreter()
    {
        var expression = """
        {
          "call": "math.clamp",
          "args": [
            {
              "call": "math.max",
              "args": [
                { "call": "math.min", "args": [{ "i32": -3 }, { "i32": 5 }] },
                { "call": "math.abs", "args": [{ "i32": -7 }] }
              ]
            },
            { "i32": 0 },
            { "i32": 6 }
          ]
        }
        """;

        var interpreted = await ExecuteReturnAsync(
            expression,
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted },
            compiler: true);
        var compiled = await ExecuteReturnAsync(
            expression,
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            compiler: true);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(((I32Value)interpreted.Value!).Value, ((I32Value)compiled.Value!).Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
        Assert.Equal(interpreted.ResourceUsage.HostCalls, compiled.ResourceUsage.HostCalls);
        Assert.Equal(4, compiled.ResourceUsage.HostCalls);
    }

    [Theory]
    [MemberData(nameof(F64Cases))]
    public async Task Compiled_f64_math_bindings_match_interpreter(string expression, double expected)
    {
        var result = await ExecuteReturnAsync(
            expression,
            "F64",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            compiler: true);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((F64Value)result.Value!).Value, precision: 10);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Fact]
    public async Task Compiled_direct_math_binding_enforces_host_call_limit()
    {
        var result = await ExecuteReturnAsync(
            """{ "call": "math.sqrt", "args": [{ "f64": 9.0 }] }""",
            "F64",
            SandboxPolicyBuilder.Create().WithMaxHostCalls(0).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            compiler: true);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_f64_math_loop_charges_each_direct_intrinsic_call()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync("""
        {
          "id": "compiled-f64-math-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "F64",
              "body": [
                { "op": "set", "name": "total", "value": { "f64": 81.0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 3 },
                  "body": [
                    { "op": "set", "name": "total", "value": { "call": "math.sqrt", "args": [{ "var": "total" }] } }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(Math.Sqrt(Math.Sqrt(Math.Sqrt(81.0))), ((F64Value)result.Value!).Value, precision: 10);
        Assert.Equal(3, result.ResourceUsage.HostCalls);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Theory]
    [InlineData("""{ "call": "math.clamp", "args": [{ "i32": 1 }, { "i32": 5 }, { "i32": 2 }] }""")]
    [InlineData("""{ "call": "math.abs", "args": [{ "i32": -2147483648 }] }""")]
    public async Task Math_domain_errors_are_sandbox_errors(string expression)
    {
        var result = await ExecuteReturnAsync(
            expression,
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
    }

    [Theory]
    [InlineData("""{ "call": "math.clamp", "args": [{ "i32": 1 }, { "i32": 5 }, { "i32": 2 }] }""")]
    [InlineData("""{ "call": "math.abs", "args": [{ "i32": -2147483648 }] }""")]
    public async Task Compiled_math_domain_errors_are_sandbox_errors(string expression)
    {
        var result = await ExecuteReturnAsync(
            expression,
            "I32",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false },
            compiler: true);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task F64_math_domain_errors_are_sandbox_errors(
        ExecutionMode mode,
        bool compiler)
    {
        var result = await ExecuteReturnAsync(
            """{ "call": "math.sqrt", "args": [{ "f64": -1.0 }] }""",
            "F64",
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false },
            compiler);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteReturnAsync(
        string expression,
        string returnType,
        SandboxPolicy policy,
        SandboxExecutionOptions options,
        bool compiler = false)
    {
        var host = SandboxTestHost.Create(compiler: compiler);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "math-bindings",
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
        """);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
    }
}
