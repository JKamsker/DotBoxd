using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Compiler.Emitters;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class Fix_CMP_0036_Tests
{
    [Theory]
    [InlineData("i32-to-i64", ExecutionMode.Interpreted)]
    [InlineData("i32-to-i64", ExecutionMode.Compiled)]
    [InlineData("i32-to-f64", ExecutionMode.Interpreted)]
    [InlineData("i32-to-f64", ExecutionMode.Compiled)]
    [InlineData("i64-to-f64", ExecutionMode.Interpreted)]
    [InlineData("i64-to-f64", ExecutionMode.Compiled)]
    public async Task Numeric_conversion_assignment_matches_interpreter(string conversion, ExecutionMode mode)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleJson(conversion));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(mode, result.ActualMode);
        AssertValue(conversion, result.Value!);
    }

    [Theory]
    [InlineData("i32-to-i64", Code.Conv_I8, "I64")]
    [InlineData("i32-to-f64", Code.Conv_R8, "F64")]
    [InlineData("i64-to-f64", Code.Conv_R8, "F64")]
    public async Task Numeric_conversion_assignment_uses_raw_il_without_unbox_call(
        string conversion,
        Code expectedConversion,
        string returnBox)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleJson(conversion));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());

        var artifact = await compiler.CompileAsync(plan, new CompileOptions("main"), CancellationToken.None);
        using var image = new MemoryStream(artifact.AssemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(image);
        var function = assembly.MainModule.Types
            .SelectMany(type => type.Methods)
            .Single(method => method.Name == "Fn_0");
        var instructions = function.Body.Instructions;

        Assert.Contains(instructions, instruction => instruction.OpCode.Code == expectedConversion);
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.AsI64)));
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.AsF64)));
        Assert.Equal(1, instructions.Count(instruction => CallsRuntime(instruction, returnBox)));
    }

    private static void AssertValue(string conversion, SandboxValue value)
    {
        switch (conversion)
        {
            case "i32-to-i64":
                Assert.Equal(123L, ((I64Value)value).Value);
                break;
            case "i32-to-f64":
                Assert.Equal(123.0, ((F64Value)value).Value);
                break;
            default:
                Assert.Equal(456.0, ((F64Value)value).Value);
                break;
        }
    }

    private static string ModuleJson(string conversion)
        => $$"""
        {
          "id": "compiled-numeric-conversion-{{conversion}}",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{ReturnType(conversion)}}",
              "body": [
                { "op": "set", "name": "converted", "value": {{ExpressionJson(conversion)}} },
                { "op": "return", "value": { "var": "converted" } }
              ]
            }
          ]
        }
        """;

    private static string ReturnType(string conversion)
        => conversion == "i32-to-i64" ? "I64" : "F64";

    private static string ExpressionJson(string conversion)
        => conversion switch
        {
            "i32-to-i64" => """{ "call": "numeric.toI64", "args": [{ "i32": 123 }] }""",
            "i32-to-f64" => """{ "call": "numeric.toF64", "args": [{ "i32": 123 }] }""",
            _ => """{ "call": "numeric.toF64", "args": [{ "i64": 456 }] }"""
        };

    private static bool CallsRuntime(Instruction instruction, string method)
        => instruction.OpCode.Code == Code.Call &&
           instruction.Operand is MethodReference {
               Name: var name,
               DeclaringType.FullName: "DotBoxD.Kernels.Runtime.CompiledRuntime"
           } &&
           string.Equals(name, method, StringComparison.Ordinal);
}
