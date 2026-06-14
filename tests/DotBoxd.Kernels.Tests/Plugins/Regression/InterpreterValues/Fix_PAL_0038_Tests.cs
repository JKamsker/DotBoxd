using DotBoxd.Kernels;
using DotBoxd.Hosting;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0038: the interpreted call dispatcher allocated a fresh
/// argument array for every <c>CallExpression</c>, including the fixed-arity collection
/// intrinsics (<c>list.*</c> / <c>map.*</c>) that callers run inside hot loops.
///
/// The fix dispatches fixed-arity collection intrinsics straight from their evaluated
/// operands (see <c>CollectionIntrinsicDispatcher</c>) without materializing a per-call
/// <see cref="SandboxValue"/>[]. These intrinsics complete synchronously and never let an
/// argument escape, so the array-free path is observationally identical to the prior
/// array-backed path. The variadic <c>list.of</c>, local functions, and host bindings keep
/// using the array path because a callee may retain the argument list.
///
/// These tests pin two properties:
/// 1. Functional equivalence: every fixed-arity collection intrinsic (including operand
///    ordering, generic-typed empties, and the out-of-range error path) still produces the
///    exact same result through the interpreter, including when an operand is itself a host
///    binding call.
/// 2. Allocation: executing many fixed-arity collection calls no longer allocates an
///    argument array per call, so steady-state per-call allocation stays below the size of
///    even a one-element <c>SandboxValue[]</c> that the bug allocated on every call.
///
/// The allocation assertion samples a thread-local byte counter around a tight measured
/// window, so it must run in the serial <see cref="AllocationMeasurementCollection"/> to keep
/// concurrent GC pressure from inflating the sample. The collection only changes scheduling;
/// it does not weaken the &lt; 64 byte/call threshold or any measured value.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class Fix_PAL_0038_Tests
{
    [Fact]
    public async Task Fixed_arity_collection_intrinsics_match_expected_results_through_interpreter()
    {
        var host = SandboxTestHost.Create();

        // Exercises list.of (array path), list.add, list.count, list.get (operand order:
        // reads arg[1] then arg[0]), map.empty (generic type), map.set, map.get, and a
        // list.get whose index operand is itself a host binding call (math.abs) so the
        // binding result flows through the fixed-arity dispatch without an argument array.
        var module = await host.ImportJsonAsync("""
        {
          "id": "interpreter-collection-pal0038",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "items",
                  "value": {
                    "call": "list.add",
                    "args": [
                      { "call": "list.of", "args": [{ "i32": 10 }, { "i32": 20 }] },
                      { "i32": 30 }
                    ]
                  }
                },
                {
                  "op": "set",
                  "name": "picked",
                  "value": {
                    "call": "list.get",
                    "args": [{ "var": "items" }, { "call": "math.abs", "args": [{ "i32": -2 }] }]
                  }
                },
                {
                  "op": "set",
                  "name": "scores",
                  "value": { "call": "map.empty", "genericType": { "name": "Map", "arguments": ["I32", "I32"] }, "args": [] }
                },
                {
                  "op": "set",
                  "name": "scores",
                  "value": {
                    "call": "map.set",
                    "args": [{ "var": "scores" }, { "i32": 1 }, { "var": "picked" }]
                  }
                },
                {
                  "op": "return",
                  "value": {
                    "op": "add",
                    "left": {
                      "op": "add",
                      "left": { "call": "list.count", "args": [{ "var": "items" }] },
                      "right": { "call": "map.get", "args": [{ "var": "scores" }, { "i32": 1 }] }
                    },
                    "right": { "var": "picked" }
                  }
                }
              ]
            }
          ]
        }
        """);

        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        var result = await ExecuteInterpretedAsync(host, plan);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);

        // items = [10, 20, 30]; picked = items[abs(-2)] = items[2] = 30; scores[1] = 30.
        // Total = list.count(items)(3) + map.get(scores,1)(30) + picked(30) = 63.
        Assert.Equal(63, ((I32Value)result.Value!).Value);
    }

    [Fact]
    public async Task List_get_out_of_range_still_fails_through_the_array_free_path()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "interpreter-collection-range-pal0038",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.get",
                    "args": [{ "call": "list.of", "args": [{ "i32": 1 }] }, { "i32": 5 }]
                  }
                }
              ]
            }
          ]
        }
        """);

        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        var result = await ExecuteInterpretedAsync(host, plan);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal(ExecutionMode.Interpreted, result.ActualMode);
    }

    [Fact]
    public async Task Repeated_fixed_arity_collection_calls_do_not_allocate_an_argument_array_per_call()
    {
        var host = SandboxTestHost.Create();

        // Two structurally identical plans that differ only in how many fixed-arity
        // collection calls (list.count over a fixed list) each execution performs. The
        // difference in per-execution allocation, divided by the extra call count, isolates
        // the steady-state allocation of a single list.count call.
        var manyPlan = await PrepareCountLoopPlanAsync(host, callsPerExecution: 2_000);
        var fewPlan = await PrepareCountLoopPlanAsync(host, callsPerExecution: 100);

        var manyPerExecution = await PerExecutionAllocationAsync(host, manyPlan);
        var fewPerExecution = await PerExecutionAllocationAsync(host, fewPlan);

        var bytesPerCall = (double)(manyPerExecution - fewPerExecution) / (2_000 - 100);

        // Each loop iteration unavoidably allocates the loop-variable I32Value plus the
        // list.count result I32Value (~24 bytes each, ~48 bytes total) in both old and new
        // code. While PAL-0038 was present each call ALSO allocated a one-element
        // SandboxValue[] (24-byte object header + one 8-byte slot = ~32 bytes), pushing
        // per-call allocation to roughly 80 bytes. Removing the per-call argument array
        // keeps per-call allocation comfortably under this 64-byte bound; the array-
        // allocating code cannot.
        Assert.True(
            bytesPerCall < 64,
            $"Interpreted list.count allocated ~{bytesPerCall:F1} bytes/call " +
            $"(many={manyPerExecution} bytes, few={fewPerExecution} bytes). A fixed-arity " +
            "collection intrinsic must not allocate a per-call argument array.");
    }

    private static async Task<long> PerExecutionAllocationAsync(SandboxHost host, ExecutionPlan plan)
    {
        for (var i = 0; i < 4; i++)
        {
            var warm = await ExecuteInterpretedAsync(host, plan);
            Assert.True(warm.Succeeded, warm.Error?.SafeMessage);
        }

        const int iterations = 16;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            _ = await ExecuteInterpretedAsync(host, plan);
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
    }

    private static async Task<ExecutionPlan> PrepareCountLoopPlanAsync(SandboxHost host, int callsPerExecution)
    {
        var module = await host.ImportJsonAsync($$"""
        {
          "id": "interpreter-count-loop-pal0038",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "set",
                  "name": "items",
                  "value": { "call": "list.of", "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }] }
                },
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": {{callsPerExecution}} },
                  "body": [
                    {
                      "op": "set",
                      "name": "total",
                      "value": { "call": "list.count", "args": [{ "var": "items" }] }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """);

        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .WithFuel(callsPerExecution * 40L + 10_000L)
                .WithMaxLoopIterations(callsPerExecution + 1)
                .Build());
    }

    private static async Task<SandboxExecutionResult> ExecuteInterpretedAsync(SandboxHost host, ExecutionPlan plan)
        => await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
}
