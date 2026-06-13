using SafeIR;
using SafeIR.Hosting;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0044: compiled collection literals allocate a fresh
/// <see cref="SandboxValue"/>[] buffer (and, for maps, rebuild a dictionary) every time the
/// literal expression executes, even though the literal graph is immutable module data.
///
/// The emitter (<c>CompiledLiteralEmitter.EmitValueArray</c>) emits a per-execution call to
/// <c>CompiledRuntime.CreateLiteralValueArray</c> which returns <c>new SandboxValue[count]</c>
/// (<c>CompiledLiteralRuntime.CreateValueArray</c>), then fills it element by element. So a
/// list literal of N elements re-allocates an N-slot array on every evaluation, and the
/// per-execution allocation of evaluating a constant literal scales with literal size.
///
/// The CORRECT behavior is that an immutable constant literal is materialized once (hoisted
/// into per-artifact literal storage) and reused, so steady-state per-execution allocation for
/// evaluating the same constant does NOT grow with the literal element count.
///
/// This test executes two structurally identical compiled entrypoints that each return a
/// constant list literal -- a large one and a tiny one -- and measures real per-execution
/// allocation after warm-up using <see cref="GC.GetAllocatedBytesForCurrentThread"/> (the
/// charged resource accounting is intentionally kept identical by design, so only real GC
/// bytes are an observable signal). It asserts the per-element growth is small. While the bug
/// is present the large literal re-allocates an N-slot value array (plus per-element value
/// objects and the returned snapshot) every execution, so per-element growth is far above the
/// threshold and the assertion is RED. It is expressed entirely against existing public APIs.
/// </summary>
public sealed class Fix_PAL_0044_Tests
{
    // Large literal stays under the default MaxListLength (10,000) while being big enough that
    // a per-element growth signal dominates fixed per-call bookkeeping and JIT noise.
    private const int LargeElementCount = 2_000;
    private const int SmallElementCount = 10;

    // A correct fix hoists the immutable literal so per-execution growth with literal size is
    // ~0 bytes/element. While the bug is present each extra element costs at least one fresh
    // reference slot in the re-allocated literal array (8 bytes on 64-bit) plus a re-allocated
    // I32Value (~24 bytes) plus the returned snapshot slot, i.e. well above this threshold.
    private const long MaxBytesPerElement = 12;

    [Fact]
    public async Task Compiled_constant_list_literal_does_not_reallocate_value_array_per_execution()
    {
        var host = SandboxTestHost.Create(compiler: true);
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000_000)
            .WithMaxAllocatedBytes(64L * 1024 * 1024)
            .WithMaxTotalCollectionElements(1_000_000)
            .Build();

        var largePlan = await PrepareLiteralPlanAsync(host, policy, LargeElementCount);
        var smallPlan = await PrepareLiteralPlanAsync(host, policy, SmallElementCount);

        var largePerExecution = await PerExecutionAllocationAsync(host, largePlan);
        var smallPerExecution = await PerExecutionAllocationAsync(host, smallPlan);

        var bytesPerElement =
            (double)(largePerExecution - smallPerExecution) / (LargeElementCount - SmallElementCount);

        // RED until PAL-0044 is fixed: evaluating a constant list literal currently re-allocates
        // an N-slot SandboxValue[] (and re-creates the value graph) on every execution, so the
        // per-execution allocation grows roughly linearly with the literal element count. Once
        // the immutable literal is hoisted and reused, evaluating the same constant must not
        // allocate storage proportional to its size on every execution.
        Assert.True(
            bytesPerElement < MaxBytesPerElement,
            $"Compiled constant list literal allocated ~{bytesPerElement:F1} bytes/element per " +
            $"execution (large={largePerExecution} bytes over {LargeElementCount} elements, " +
            $"small={smallPerExecution} bytes over {SmallElementCount} elements). A hoisted " +
            "immutable literal should not re-allocate a value array proportional to its size on " +
            "every evaluation.");
    }

    private static async Task<long> PerExecutionAllocationAsync(SandboxHost host, ExecutionPlan plan)
    {
        // Warm up: JIT the compiled method and materialize the artifact so the measured loop
        // reflects only steady-state per-execution cost.
        for (var i = 0; i < 4; i++)
        {
            var warm = await ExecuteCompiledAsync(host, plan);
            Assert.True(warm.Succeeded, warm.Error?.SafeMessage);
            Assert.Equal(ExecutionMode.Compiled, warm.ActualMode);
        }

        const int iterations = 32;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            _ = await ExecuteCompiledAsync(host, plan);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private static async Task<ExecutionPlan> PrepareLiteralPlanAsync(
        SandboxHost host,
        SandboxPolicy policy,
        int elementCount)
    {
        var module = LiteralReturnModule(elementCount);
        return await host.PrepareAsync(module, policy);
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiledAsync(SandboxHost host, ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

    private static SandboxModule LiteralReturnModule(int elementCount)
    {
        var span = new SourceSpan(0, 0);

        var elements = new SandboxValue[elementCount];
        for (var i = 0; i < elementCount; i++)
        {
            elements[i] = SandboxValue.FromInt32(i);
        }

        var literal = new LiteralExpression(
            SandboxValue.FromList(elements, SandboxType.I32),
            span);

        return new SandboxModule(
            "compiled-literal-pal0044",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.List(SandboxType.I32),
                    [new ReturnStatement(literal, span)])
            ],
            new Dictionary<string, string>());
    }
}
