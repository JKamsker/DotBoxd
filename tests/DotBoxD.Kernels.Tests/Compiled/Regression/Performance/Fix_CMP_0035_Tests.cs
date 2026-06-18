using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class Fix_CMP_0035_Tests
{
    [Fact]
    public async Task Compiled_I64_unary_negation_matches_interpreter()
    {
        var interpreted = await ExecuteAsync(
            "I64",
            """{ "unary": "-", "operand": { "i64": 42 } }""",
            ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(
            "I64",
            """{ "unary": "-", "operand": { "i64": 42 } }""",
            ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(-42, ((I64Value)compiled.Value!).Value);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task I64_unary_negation_min_value_fails_with_invalid_input(ExecutionMode mode)
    {
        var result = await ExecuteAsync(
            "I64",
            """{ "unary": "-", "operand": { "i64": -9223372036854775808 } }""",
            mode);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    [Fact]
    public async Task Compiled_F64_unary_negation_preserves_negative_zero()
    {
        var interpreted = await ExecuteAsync(
            "F64",
            """{ "unary": "-", "operand": { "f64": 0.0 } }""",
            ExecutionMode.Interpreted);
        var compiled = await ExecuteAsync(
            "F64",
            """{ "unary": "-", "operand": { "f64": 0.0 } }""",
            ExecutionMode.Compiled);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(NegativeZeroBits, Bits(compiled));
        Assert.Equal(Bits(interpreted), Bits(compiled));
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    private const long NegativeZeroBits = unchecked((long)0x8000000000000000);

    private static long Bits(SandboxExecutionResult result)
        => BitConverter.DoubleToInt64Bits(((F64Value)result.Value!).Value);

    private static async Task<SandboxExecutionResult> ExecuteAsync(
        string returnType,
        string expression,
        ExecutionMode mode)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleJson(returnType, expression));
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static string ModuleJson(string returnType, string expression)
        => $$"""
        {
          "id": "compiled-raw-unary-negation",
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
