using SafeIR;
using SafeIR.Hosting;

namespace SafeIR.Tests;

/// <summary>
/// Regression coverage for PAL-0042: the interpreter allocates a string-keyed
/// local <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> for
/// every function invocation. <c>InterpreterFrame.Create</c> does
/// <c>new Dictionary&lt;string, SandboxValue&gt;(StringComparer.Ordinal)</c> on
/// each call, so a helper invoked inside a loop pays one fresh dictionary object
/// per iteration even when the function has a fixed, known shape.
///
/// The test isolates that per-call frame cost with a difference-of-slopes
/// measurement that only public host APIs can observe:
///   1. Two interpreted plans run the same loop; one accumulates the result of a
///      zero-parameter helper call each iteration, the other accumulates an
///      inline literal (no call). Every other per-iteration allocation (the loop
///      index value, the operand value, and the add result) is identical, so it
///      cancels.
///   2. For each plan the per-iteration allocation slope is taken by differencing
///      two iteration counts driven through the entrypoint parameter, which
///      removes all fixed per-run setup from the measurement.
///   3. The difference of the two slopes is the allocation attributable purely to
///      one extra helper invocation: today that includes the per-call local
///      dictionary object.
///
/// The correct (post-fix) behavior binds parameters and locals into a prepared,
/// indexed frame shape instead of allocating a string-keyed dictionary per call,
/// so the per-call overhead must stay below the size of one empty dictionary
/// instance. The assertion is RED while every frame allocates its own dictionary.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class Fix_PAL_0042_Tests
{
    // Two iteration counts whose difference is large enough that one allocation
    // per invocation is far above measurement noise once divided out.
    private const int LowIterations = 2_000;
    private const int HighIterations = 12_000;
    private const int IterationSpan = HighIterations - LowIterations;

    // An empty Dictionary<string, SandboxValue> instance (object header plus the
    // bucket/entry references, counters, and comparer reference) costs on the
    // order of this many bytes on a 64-bit runtime, even before any entry forces
    // a backing array. The fix removes this per-call object, so the isolated
    // per-call overhead must drop below this floor. The interpreter frame object
    // and the caller-side argument list (PAL-0038, out of scope here) remain and
    // are well under this bound.
    private const long EmptyDictionaryFloorBytes = 56L;

    [Fact]
    public async Task Helper_invocation_in_a_loop_does_not_allocate_a_per_call_local_dictionary()
    {
        var callPlan = await PreparePlanAsync(HelperCallModuleJson);
        var inlinePlan = await PreparePlanAsync(InlineModuleJson);

        var perCallIteration = await PerIterationAllocationAsync(callPlan);
        var perInlineIteration = await PerIterationAllocationAsync(inlinePlan);

        // Everything except the helper invocation is structurally identical
        // between the two loops, so the difference is the allocation cost of one
        // extra interpreter frame per iteration.
        var perInvocationBytes = perCallIteration - perInlineIteration;

        Assert.True(
            perInvocationBytes < EmptyDictionaryFloorBytes,
            $"Each helper invocation allocates a string-keyed local dictionary: " +
            $"call loop = {perCallIteration} bytes/iter, inline loop = {perInlineIteration} bytes/iter, " +
            $"per-invocation overhead = {perInvocationBytes} bytes (>= {EmptyDictionaryFloorBytes}). " +
            $"InterpreterFrame.Create allocates a Dictionary<string, SandboxValue> for every call.");
    }

    private static async Task<long> PerIterationAllocationAsync(ExecutionPlan plan)
    {
        // Warm up JIT and any one-time interpreter caches so the measured window
        // only reflects steady-state per-iteration allocation.
        await RunAsync(plan, LowIterations);
        await RunAsync(plan, HighIterations);

        var lowBytes = await MeasureRunBytesAsync(plan, LowIterations);
        var highBytes = await MeasureRunBytesAsync(plan, HighIterations);

        // Differencing two iteration counts cancels the fixed per-run setup and
        // leaves the per-iteration allocation slope.
        return (highBytes - lowBytes) / IterationSpan;
    }

    private static async Task<long> MeasureRunBytesAsync(ExecutionPlan plan, int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        await RunAsync(plan, iterations);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static async Task RunAsync(ExecutionPlan plan, int iterations)
    {
        var host = HostHolder.Value;
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        // Each iteration adds 1, so the entrypoint returns the iteration count:
        // confirms the helper actually executed every loop pass.
        Assert.Equal(iterations, ((I32Value)result.Value!).Value);
    }

    private static async Task<ExecutionPlan> PreparePlanAsync(string moduleJson)
    {
        var host = HostHolder.Value;
        var module = await host.ImportJsonAsync(moduleJson);
        return await host.PrepareAsync(module, BudgetPolicy());
    }

    private static SandboxPolicy BudgetPolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(50_000_000)
            .WithMaxLoopIterations(1_000_000)
            .WithMaxAllocatedBytes(1_000_000_000)
            .Build();

    private static readonly Lazy<SandboxHost> HostHolder = new(() => SandboxHost.Create(builder =>
    {
        builder.AddDefaultPureBindings();
        builder.UseInterpreter();
    }));

    private const string HelperCallModuleJson = """
    {
      "id": "pal-0042-helper-call",
      "version": "1.0.0",
      "targetSandboxVersion": "1.0.0",
      "capabilityRequests": [],
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [{ "name": "n", "type": "I32" }],
          "returnType": "I32",
          "body": [
            { "op": "set", "name": "sum", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "var": "n" },
              "body": [
                {
                  "op": "set",
                  "name": "sum",
                  "value": {
                    "op": "add",
                    "left": { "var": "sum" },
                    "right": { "call": "helper", "args": [] }
                  }
                }
              ]
            },
            { "op": "return", "value": { "var": "sum" } }
          ]
        },
        {
          "id": "helper",
          "visibility": "private",
          "parameters": [],
          "returnType": "I32",
          "body": [{ "op": "return", "value": { "i32": 1 } }]
        }
      ]
    }
    """;

    private const string InlineModuleJson = """
    {
      "id": "pal-0042-inline",
      "version": "1.0.0",
      "targetSandboxVersion": "1.0.0",
      "capabilityRequests": [],
      "functions": [
        {
          "id": "main",
          "visibility": "entrypoint",
          "parameters": [{ "name": "n", "type": "I32" }],
          "returnType": "I32",
          "body": [
            { "op": "set", "name": "sum", "value": { "i32": 0 } },
            {
              "op": "forRange",
              "local": "i",
              "start": { "i32": 0 },
              "end": { "var": "n" },
              "body": [
                {
                  "op": "set",
                  "name": "sum",
                  "value": {
                    "op": "add",
                    "left": { "var": "sum" },
                    "right": { "i32": 1 }
                  }
                }
              ]
            },
            { "op": "return", "value": { "var": "sum" } }
          ]
        }
      ]
    }
    """;
}
