namespace DotBoxd.Kernels.Benchmarks.Interpreter;

using System.Diagnostics;
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Serialization.Json;

/// <summary>
/// Stopwatch probe for repeated IR-to-managed pure binding crossings. The loop body calls
/// <c>math.sqrt</c>, so compiled code must either cross the generic binding dispatcher or use
/// a direct verified intrinsic stub. Run with <c>dotnet run -c Release -- --probe-bindings</c>.
/// </summary>
internal static class BindingCrossingProbe
{
    public static async Task RunAsync()
    {
        const int iterations = 2_000_000;
        const int warmup = 100_000;

        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(long.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxAllocatedBytes(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        _ = Handwritten(warmup);
        _ = await RunSandbox(host, plan, warmup, ExecutionMode.Compiled);
        _ = await RunSandbox(host, plan, warmup, ExecutionMode.Interpreted);

        var handwrittenMs = Time(() => Handwritten(iterations));
        var compiledMs = await TimeAsync(() => RunSandbox(host, plan, iterations, ExecutionMode.Compiled));
        var interpretedMs = await TimeAsync(() => RunSandbox(host, plan, iterations, ExecutionMode.Interpreted));

        Console.WriteLine($"iterations = {iterations:N0}");
        Console.WriteLine($"handwritten C# : {handwrittenMs,8:N1} ms  (1.0x)");
        Console.WriteLine($"compiled IL    : {compiledMs,8:N1} ms  ({compiledMs / handwrittenMs,5:N1}x)");
        Console.WriteLine($"interpreted    : {interpretedMs,8:N1} ms  ({interpretedMs / handwrittenMs,5:N1}x)");
    }

    private static double Handwritten(int iterations)
    {
        var total = 1.0;
        for (var i = 0; i < iterations; i++)
        {
            total = Math.Sqrt(total);
        }

        return total;
    }

    private static async Task<SandboxValue?> RunSandbox(
        SandboxHost host,
        ExecutionPlan plan,
        int iterations,
        ExecutionMode mode)
    {
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false });
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
        }

        return result.Value;
    }

    private static double Time(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static async Task<double> TimeAsync(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static string ModuleJson()
        => """
        {
          "id": "binding-crossing-probe",
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
                      "op": "set",
                      "name": "total",
                      "value": { "call": "math.sqrt", "args": [{ "var": "total" }] }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
