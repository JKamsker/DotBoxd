using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier.Generated;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance;

public sealed class AccumulatorClosedFormParityTests
{
    [Fact]
    public async Task Modulo_branch_accumulator_uses_closed_form_and_matches_interpreter()
    {
        var interpreted = await RunAsync(ModuloBranchModuleJson(), ExecutionMode.Interpreted, 5);
        var compiled = await RunAsync(ModuloBranchModuleJson(), ExecutionMode.Compiled, 5);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(7, ((I32Value)compiled.Value!).Value);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(1, compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed);

        var instructions = await CompileInstructionsAsync(ModuloBranchModuleJson());
        Assert.Contains(instructions, i => CallsRuntime(i, nameof(CompiledRuntime.AddModuloBranchDeltasI32LoopRaw)));
    }

    [Fact]
    public async Task Modulo_branch_overflow_falls_back_with_matching_failure_accounting()
    {
        var interpreted = await RunAsync(ModuloBranchOverflowModuleJson(), ExecutionMode.Interpreted, 1);
        var compiled = await RunAsync(ModuloBranchOverflowModuleJson(), ExecutionMode.Compiled, 1);

        AssertError(interpreted, ExecutionMode.Interpreted, SandboxErrorCode.InvalidInput);
        AssertError(compiled, ExecutionMode.Compiled, SandboxErrorCode.InvalidInput);
        Assert.Equal(interpreted.ResourceUsage.LoopIterations, compiled.ResourceUsage.LoopIterations);
        Assert.Equal(interpreted.ResourceUsage.FuelUsed, compiled.ResourceUsage.FuelUsed);
    }

    [Fact]
    public async Task Modulo_index_while_accumulator_uses_closed_form_and_matches_interpreter()
    {
        var interpreted = await RunAsync(ModuloIndexWhileModuleJson(initialTotal: 0, divisor: 1_000_003), ExecutionMode.Interpreted, 8);
        var compiled = await RunAsync(ModuloIndexWhileModuleJson(initialTotal: 0, divisor: 1_000_003), ExecutionMode.Compiled, 8);

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(28, ((I32Value)compiled.Value!).Value);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(1, compiled.ResourceUsage.FuelUsed - interpreted.ResourceUsage.FuelUsed);

        var instructions = await CompileInstructionsAsync(ModuloIndexWhileModuleJson(initialTotal: 0, divisor: 1_000_003));
        Assert.Contains(instructions, i => CallsRuntime(i, nameof(CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw)));
    }

    [Fact]
    public void Modulo_index_arithmetic_series_matches_step_loop_for_valid_domain()
    {
        for (var divisor = 1; divisor <= 11; divisor++)
            for (var current = 0; current < divisor; current++)
                for (var start = 0; start <= 12; start++)
                    for (var count = 1; count <= 12; count++)
                    {
                        var end = start + count;
                        var context = Context();

                        Assert.True(CompiledRuntime.CanUseModuloIndexAccumulatorRaw(context, current, start, end, divisor, 3, 15));
                        var actual = CompiledRuntime.AddModuloIndexAccumulatorI32LoopRaw(context, current, start, end, divisor, 3, 15);

                        Assert.Equal(ExpectedModuloIndex(current, start, end, divisor), actual);
                    }
    }

    [Fact]
    public void Remainder_cycle_helper_charges_only_through_overflow_iteration()
    {
        var context = Context();

        var error = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.ListI32ReaderAddRemainderCycleFromZeroRaw(
                context,
                new[] { int.MaxValue },
                current: 0,
                iterations: 5,
                divisor: 1,
                loopFuelPerIteration: 10,
                readFuel: 3));

        Assert.Equal(SandboxErrorCode.InvalidInput, error.Error.Code);
        Assert.Equal(2, context.Budget.LoopIterations);
        Assert.Equal(26, context.Budget.FuelUsed);
    }

    [Fact]
    public void Remainder_cycle_helper_charges_only_through_invalid_index_iteration()
    {
        var context = Context();

        var error = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.ListI32ReaderAddRemainderCycleFromZeroRaw(
                context,
                new[] { 7 },
                current: 0,
                iterations: 5,
                divisor: 2,
                loopFuelPerIteration: 10,
                readFuel: 3));

        Assert.Equal(SandboxErrorCode.InvalidInput, error.Error.Code);
        Assert.Equal(2, context.Budget.LoopIterations);
        Assert.Equal(26, context.Budget.FuelUsed);
    }

    private static int ExpectedModuloIndex(int current, int start, int end, int divisor)
    {
        var total = current;
        for (var i = start; i < end; i++)
        {
            total = (total + i) % divisor;
        }

        return total;
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

    private static SandboxContext Context()
    {
        var limits = new ResourceLimits(MaxFuel: 1_000_000, MaxLoopIterations: 1_000_000);
        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private static void AssertError(SandboxExecutionResult result, ExecutionMode mode, SandboxErrorCode code)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static string ModuloBranchModuleJson()
        => """
        {
          "id": "modulo-branch-accumulator",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "total", "value": { "i32": 0 } },
              { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                "body": [{ "op": "if",
                  "condition": { "op": "eq", "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 2 } }, "right": { "i32": 0 } },
                  "then": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 1 } } }],
                  "else": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 2 } } }]
                }] },
              { "op": "return", "value": { "var": "total" } }
            ]
          }]
        }
        """;

    private static string ModuloBranchOverflowModuleJson()
        => """
        {
          "id": "modulo-branch-overflow",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "total", "value": { "i32": 2147483647 } },
              { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                "body": [{ "op": "if",
                  "condition": { "op": "eq", "left": { "op": "rem", "left": { "var": "i" }, "right": { "i32": 1 } }, "right": { "i32": 0 } },
                  "then": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 1 } } }],
                  "else": [{ "op": "set", "name": "total", "value": { "op": "add", "left": { "var": "total" }, "right": { "i32": 1 } } }]
                }] },
              { "op": "return", "value": { "var": "total" } }
            ]
          }]
        }
        """;

    private static string ModuloIndexWhileModuleJson(int initialTotal, int divisor)
        => $$"""
        {
          "id": "modulo-index-while-accumulator",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [{ "name": "iterations", "type": "I32" }],
            "returnType": "I32",
            "body": [
              { "op": "set", "name": "i", "value": { "i32": 0 } },
              { "op": "set", "name": "total", "value": { "i32": {{initialTotal}} } },
              { "op": "while", "condition": { "op": "lt", "left": { "var": "i" }, "right": { "var": "iterations" } },
                "body": [
                  { "op": "set", "name": "total", "value": {
                    "op": "rem",
                    "left": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } },
                    "right": { "i32": {{divisor}} } } },
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
