using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class F64MathIntrinsicAssignmentTests
{
    public static TheoryData<string, double> Cases()
        => new() {
            { """{ "call": "math.sqrt", "args": [{ "f64": 9.0 }] }""", 3.0 },
            { """{ "call": "math.floor", "args": [{ "f64": 3.8 }] }""", 3.0 },
            { """{ "call": "math.ceil", "args": [{ "f64": 3.2 }] }""", 4.0 },
            { """{ "call": "math.round", "args": [{ "f64": 2.6 }] }""", 3.0 }
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Compiled_f64_math_assignment_bindings_match_interpreter(string expression, double expected)
    {
        var result = await ExecuteAssignmentAsync(
            expression,
            SandboxPolicyBuilder.Create().Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(expected, ((F64Value)result.Value!).Value, precision: 10);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Compiled_f64_math_assignment_charges_host_call_limit(string expression, double _)
    {
        var result = await ExecuteAssignmentAsync(
            expression,
            SandboxPolicyBuilder.Create().WithMaxHostCalls(0).Build(),
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    private static async Task<SandboxExecutionResult> ExecuteAssignmentAsync(
        string expression,
        SandboxPolicy policy,
        SandboxExecutionOptions options)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "f64-math-intrinsic-assignment",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "F64",
              "body": [
                { "op": "set", "name": "value", "value": {{expression}} },
                { "op": "return", "value": { "var": "value" } }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);
    }
}
