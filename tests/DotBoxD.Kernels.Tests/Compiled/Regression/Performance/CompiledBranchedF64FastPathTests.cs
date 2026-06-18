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

public sealed class CompiledBranchedF64FastPathTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Branched_F64_loop_matches_interpreter(ExecutionMode mode)
    {
        const int iterations = 100;
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(20_000)
                .WithMaxLoopIterations(1_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(Expected(iterations), ((F64Value)result.Value!).Value, precision: 10);
        Assert.Equal(mode, result.ActualMode);
    }

    [Fact]
    public async Task Branched_F64_loop_uses_bulk_loop_fast_path()
    {
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(20_000).Build());
        var compiler = new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());

        var artifact = await compiler.CompileAsync(plan, new CompileOptions("main"), CancellationToken.None);
        using var image = new MemoryStream(artifact.AssemblyBytes);
        using var assembly = AssemblyDefinition.ReadAssembly(image);
        var function = assembly.MainModule.Types
            .SelectMany(type => type.Methods)
            .Single(method => method.Name == "Fn_0");
        var instructions = function.Body.Instructions;

        Assert.Contains(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.MulF64Raw)));
        Assert.Contains(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.AddF64Raw)));
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.Mul)));
        Assert.DoesNotContain(instructions, instruction => CallsRuntime(instruction, nameof(CompiledRuntime.Add)));
        Assert.Equal(1, instructions.Count(instruction => CallsRuntime(instruction, nameof(CompiledRuntime.ChargeLoopIteration))));
        Assert.True(
            instructions.Count(instruction => CallsRuntime(instruction, nameof(CompiledRuntime.ChargeFuel))) <= 12,
            "branched f64 fast path should bulk-charge the condition and branch bodies");
    }

    [Fact]
    public async Task Branched_F64_loop_matches_interpreter_bitwise_and_per_iteration_fuel()
    {
        var interpretedSmall = await RunAsync(ExecutionMode.Interpreted, 50);
        var interpretedLarge = await RunAsync(ExecutionMode.Interpreted, 100);
        var compiledSmall = await RunAsync(ExecutionMode.Compiled, 50);
        var compiledLarge = await RunAsync(ExecutionMode.Compiled, 100);

        Assert.True(interpretedSmall.Succeeded, interpretedSmall.Error?.SafeMessage);
        Assert.True(interpretedLarge.Succeeded, interpretedLarge.Error?.SafeMessage);
        Assert.True(compiledSmall.Succeeded, compiledSmall.Error?.SafeMessage);
        Assert.True(compiledLarge.Succeeded, compiledLarge.Error?.SafeMessage);

        // The raw-IL fast path must reproduce the interpreter's f64 result to the last bit (not just 10 decimals).
        Assert.Equal(
            BitConverter.DoubleToInt64Bits(((F64Value)interpretedLarge.Value!).Value),
            BitConverter.DoubleToInt64Bits(((F64Value)compiledLarge.Value!).Value));

        // Compiled execution adds a fixed per-run fuel overhead, so absolute fuel differs by a constant; the
        // constant cancels in the delta between two iteration counts. The marginal fuel charged per loop iteration
        // must stay identical even though the fast path bulk-charges the condition and branch bodies.
        Assert.Equal(
            interpretedLarge.ResourceUsage.FuelUsed - interpretedSmall.ResourceUsage.FuelUsed,
            compiledLarge.ResourceUsage.FuelUsed - compiledSmall.ResourceUsage.FuelUsed);
    }

    private static async Task<SandboxExecutionResult> RunAsync(ExecutionMode mode, int iterations)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleJson());
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(20_000).WithMaxLoopIterations(1_000).Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
    }

    private static double Expected(int iterations)
    {
        var total = 1.0;
        for (var i = 0; i < iterations; i++)
        {
            total = i % 2 < 1
                ? (total * 0.9) + 0.1
                : (total * 0.8) + 0.2;
        }

        return total;
    }

    private static string ModuleJson()
        => """
        {
          "id": "compiled-branched-f64-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "F64",
              "body": [
                { "op": "set", "name": "total", "value": { "f64": 1.0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "var": "iterations" },
                  "body": [
                    {
                      "op": "if",
                      "condition": {
                        "op": "lt",
                        "left": {
                          "op": "rem",
                          "left": { "var": "i" },
                          "right": { "i32": 2 }
                        },
                        "right": { "i32": 1 }
                      },
                      "then": [{
                        "op": "set",
                        "name": "total",
                        "value": {
                          "op": "add",
                          "left": {
                            "op": "mul",
                            "left": { "var": "total" },
                            "right": { "f64": 0.9 }
                          },
                          "right": { "f64": 0.1 }
                        }
                      }],
                      "else": [{
                        "op": "set",
                        "name": "total",
                        "value": {
                          "op": "add",
                          "left": {
                            "op": "mul",
                            "left": { "var": "total" },
                            "right": { "f64": 0.8 }
                          },
                          "right": { "f64": 0.2 }
                        }
                      }]
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
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
