using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class BulkMeteredLoopFastPathTests
{
    [Fact]
    public async Task General_bulk_metered_path_handles_branched_i64_loop()
    {
        var interpreted = await RunAsync(BranchedI64ModuleJson(), ExecutionMode.Interpreted, 25);
        var compiled = await RunAsync(BranchedI64ModuleJson(), ExecutionMode.Compiled, 25);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(1, compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed);

        var instructions = await CompileInstructionsAsync(BranchedI64ModuleJson());
        Assert.Contains(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.MulI64Raw)));
        Assert.Contains(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.RemI64Raw)));
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.Mul)));
        Assert.Equal(1, instructions.Count(instruction => CallsRuntime(instruction, nameof(CompiledRuntime.ChargeLoopIteration))));
    }

    [Fact]
    public async Task General_bulk_metered_path_handles_while_f64_loop()
    {
        var interpreted = await RunAsync(WhileF64ModuleJson(), ExecutionMode.Interpreted, 25);
        var compiled = await RunAsync(WhileF64ModuleJson(), ExecutionMode.Compiled, 25);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(((F64Value)interpreted.Value!).Value),
            BitConverter.DoubleToInt64Bits(((F64Value)compiled.Value!).Value));
        Assert.Equal(1, compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed);

        var instructions = await CompileInstructionsAsync(WhileF64ModuleJson());
        Assert.Contains(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.MulF64Raw)));
        Assert.Contains(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.AddF64Raw)));
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.Add)));
        Assert.Equal(1, instructions.Count(instruction => CallsRuntime(instruction, nameof(CompiledRuntime.ChargeLoopIteration))));
    }

    private static async Task<SandboxExecutionResult> RunAsync(
        string moduleJson,
        ExecutionMode mode,
        int iterations)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(20_000)
                .WithMaxLoopIterations(1_000)
                .Build());

        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static async Task<IReadOnlyList<Instruction>> CompileInstructionsAsync(string moduleJson)
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(20_000).Build());
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        var artifact = await compiler.CompileAsync(plan, new CompileOptions("main"), CancellationToken.None);
        using var image = new MemoryStream(artifact.AssemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(image);
        return assembly.MainModule.Types
            .SelectMany(type => type.Methods)
            .Single(method => method.Name == "Fn_0")
            .Body
            .Instructions
            .ToArray();
    }

    private static string BranchedI64ModuleJson()
        => """
        {
          "id": "bulk-branched-i64",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I64",
            "body": [
              { "op": "set", "name": "total", "value": { "i64": 1 } },
              { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                "body": [{
                  "op": "if",
                  "condition": { "op": "lt", "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } }, "right": { "i32": 1 } },
                  "then": [{ "op": "set", "name": "total", "value": {
                    "op": "rem",
                    "left": { "op": "add", "left": { "op": "mul", "left": { "var": "total" }, "right": { "i64": 5 } }, "right": { "i64": 7 } },
                    "right": { "i64": 1000003 } } }],
                  "else": [{ "op": "set", "name": "total", "value": {
                    "op": "rem",
                    "left": { "op": "add", "left": { "op": "mul", "left": { "var": "total" }, "right": { "i64": 3 } }, "right": { "i64": 11 } },
                    "right": { "i64": 1000003 } } }]
                }] },
              { "op": "return", "value": { "var": "total" } }
            ]
          }]
        }
        """;

    private static string WhileF64ModuleJson()
        => """
        {
          "id": "bulk-while-f64",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "F64",
            "body": [
              { "op": "set", "name": "i", "value": { "i32": 0 } },
              { "op": "set", "name": "total", "value": { "f64": 1.0 } },
              { "op": "while", "condition": { "op": "lt", "left": { "var": "i" }, "right": { "var": "iterations" } },
                "body": [
                  { "op": "set", "name": "total", "value": {
                    "op": "add",
                    "left": { "op": "mul", "left": { "var": "total" }, "right": { "f64": 0.9 } },
                    "right": { "f64": 0.1 } } },
                  { "op": "set", "name": "i", "value": { "op": "add", "left": { "var": "i" }, "right": { "i32": 1 } } }
                ] },
              { "op": "return", "value": { "var": "total" } }
            ]
          }]
        }
        """;

    private static bool CallsRuntime(Instruction instruction, string method)
        => instruction.OpCode.Code == Code.Call &&
           instruction.Operand is MethodReference
           {
               Name: var name,
               DeclaringType.FullName: "DotBoxD.Kernels.Runtime.CompiledRuntime"
           } &&
           string.Equals(name, method, StringComparison.Ordinal);
}
