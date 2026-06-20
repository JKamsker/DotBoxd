using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

using System.Diagnostics;
using DotBoxD.Kernels;

/// <summary>
/// Hunts for "rogue" invocations: code whose absolute wall-time grows super-linearly with input size,
/// the kind that turns a benign-looking plugin into a multi-second stall. Constant-factor ratios against
/// handwritten code are not the concern here; algorithmic blowups are. Each pattern is run at doubling
/// sizes so the scaling exponent is visible (time roughly x4 per size doubling == quadratic).
/// </summary>
internal static class RogueScalingProbe
{
    private static readonly int[] ListSizes = [4_000, 8_000, 16_000, 32_000, 64_000];
    private static readonly int[] MapSizes = [4_000, 8_000, 16_000];

    public static async Task RunAsync()
    {
        var host = Hosting.Execution.SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxTotalCollectionElements(long.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithMaxListLength(int.MaxValue)
            .WithMaxMapEntries(int.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

        await RunPattern(host, policy, "list.add build", ListBuildJson(), ListSizes);
        await RunPattern(host, policy, "map.set build", MapBuildJson(), MapSizes);
    }

    private static async Task RunPattern(Hosting.Execution.SandboxHost host, SandboxPolicy policy, string name, string moduleJson, int[] sizes)
    {
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);

        Console.WriteLine($"== {name} ==");
        Console.WriteLine("    n   compiled   x/prev   interpreted   x/prev");
        double prevCompiled = 0, prevInterpreted = 0;
        foreach (var n in sizes)
        {
            // Warm up at this size once so JIT/first-call costs are not charged to the measurement.
            _ = await RunSandbox(host, plan, n, ExecutionMode.Compiled);
            _ = await RunSandbox(host, plan, n, ExecutionMode.Interpreted);

            var compiled = await TimeAsync(() => RunSandbox(host, plan, n, ExecutionMode.Compiled));
            var interpreted = await TimeAsync(() => RunSandbox(host, plan, n, ExecutionMode.Interpreted));
            var cScale = prevCompiled > 0 ? compiled / prevCompiled : 0;
            var iScale = prevInterpreted > 0 ? interpreted / prevInterpreted : 0;
            Console.WriteLine(
                $"{n,7} {compiled,8:N1}ms {cScale,7:N2} {interpreted,10:N1}ms {iScale,7:N2}");
            prevCompiled = compiled;
            prevInterpreted = interpreted;
        }

        Console.WriteLine();
    }

    private static async Task<SandboxValue?> RunSandbox(Hosting.Execution.SandboxHost host, ExecutionPlan plan, int n, ExecutionMode mode)
    {
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(n),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"{mode} failed: {result.Error?.SafeMessage}");
        }

        return result.Value;
    }

    private static async Task<double> TimeAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static string ListBuildJson()
        => """
        {
          "id": "rogue-list-build",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "acc", "value": { "call": "list.empty", "genericType": "I32", "args": [] } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "set", "name": "acc", "value": { "call": "list.add", "args": [ { "var": "acc" }, { "var": "i" } ] } }
                  ] },
                { "op": "return", "value": { "call": "list.count", "args": [ { "var": "acc" } ] } }
              ]
            }
          ]
        }
        """;

    private static string MapBuildJson()
        => """
        {
          "id": "rogue-map-build",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "acc", "value": { "call": "map.empty", "genericType": { "name": "Map", "arguments": ["I32", "I32"] }, "args": [] } },
                { "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "set", "name": "acc", "value": { "call": "map.set", "args": [ { "var": "acc" }, { "var": "i" }, { "var": "i" } ] } }
                  ] },
                { "op": "return", "value": { "call": "map.get", "args": [ { "var": "acc" }, { "i32": 0 } ] } }
              ]
            }
          ]
        }
        """;
}
