using System.Text;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Interpreter;

namespace DotBoxd.Kernels.Tests;

// Regression coverage for PAL-0043: interpreted runs rebuild the module-wide
// function lookup dictionary on every ExecuteAsync call. The function set of a
// prepared ExecutionPlan is immutable, so executing the same plan repeatedly
// must not pay O(function-count) dictionary allocation before each run. We
// assert this by comparing the steady-state per-run allocation of a plan with
// a single function against an otherwise-identical plan that carries thousands
// of never-invoked helper functions and a cheap entrypoint. When the lookup is
// rebuilt per run, the large plan's per-run allocation scales with the helper
// count and dwarfs the small plan's; when the lookup is built once and reused,
// the two per-run figures stay close.
public sealed class Fix_PAL_0043_Tests
{
    private const int HelperFunctionCount = 4_000;
    private const int Iterations = 200;

    [Fact]
    public async Task Repeated_interpreted_runs_do_not_allocate_per_function_in_the_module()
    {
        var interpreter = new SandboxInterpreter();

        // The "main" entrypoint declares no parameters, so its input must be Unit;
        // EntrypointBinder rejects any other shape (including an empty list).
        var input = SandboxValue.Unit;

        var smallPlan = await PreparePlanAsync(helperFunctionCount: 0);
        var largePlan = await PreparePlanAsync(HelperFunctionCount);

        var smallPerRun = MeasurePerRunAllocation(interpreter, smallPlan, input);
        var largePerRun = MeasurePerRunAllocation(interpreter, largePlan, input);

        // Both plans run the same trivial entrypoint and touch zero helper
        // functions, so per-run allocation should be effectively independent of
        // module size. We allow generous slack for unrelated per-run state, but
        // a per-run module index rebuild for thousands of functions blows far
        // past this bound.
        var allowedCeiling = (smallPerRun * 4) + 4_096;

        Assert.True(
            largePerRun <= allowedCeiling,
            $"Per-run interpreted allocation scales with module function count. " +
            $"small={smallPerRun} bytes/run, large({HelperFunctionCount} helpers)={largePerRun} bytes/run, " +
            $"allowed<={allowedCeiling}. The function lookup is being rebuilt on every execution.");
    }

    private static long MeasurePerRunAllocation(
        ISandboxInterpreter interpreter,
        ExecutionPlan plan,
        SandboxValue input)
    {
        // Warm up JIT and any first-call caches so the measured window only
        // contains steady-state per-execution work.
        for (var i = 0; i < 16; i++)
        {
            Run(interpreter, plan, input);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++)
        {
            Run(interpreter, plan, input);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return allocated / Iterations;
    }

    private static void Run(ISandboxInterpreter interpreter, ExecutionPlan plan, SandboxValue input)
    {
        var result = interpreter
            .ExecuteAsync(plan, "main", input, new SandboxExecutionOptions(), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.True(result.Succeeded);
    }

    private static async Task<ExecutionPlan> PreparePlanAsync(int helperFunctionCount)
    {
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
        });

        var module = await host.ImportJsonAsync(BuildModuleJson(helperFunctionCount));
        return await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000_000).Build());
    }

    private static string BuildModuleJson(int helperFunctionCount)
    {
        var builder = new StringBuilder();
        builder.Append("""
        {
          "id": "pal-0043",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "capabilityRequests": [],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 0 } }]
            }
        """);

        for (var i = 0; i < helperFunctionCount; i++)
        {
            builder.Append(',');
            builder.Append($$"""
            {
              "id": "helper_{{i}}",
              "visibility": "private",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": {{i}} } }]
            }
            """);
        }

        builder.Append("] }");
        return builder.ToString();
    }
}
