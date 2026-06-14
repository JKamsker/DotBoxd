namespace DotBoxd.Kernels.Benchmarks.Interpreter;

using System.Diagnostics;
using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Kernels.Serialization.Json;

/// <summary>
/// Quick stopwatch probe comparing a tight scalar loop (<c>total = (total + i) % 1_000_003</c>) across
/// handwritten C#, the DotBoxd.Kernels compiler (verified IL), and the interpreter. Used to gauge how close compiled
/// IR runs to handwritten code. This is a deliberate worst case: the loop body is two arithmetic ops, so the
/// mandatory per-operation safety metering (fuel + loop-iteration charges) dominates and sets the floor on
/// how close compiled can get to unmetered handwritten code. Run with
/// <c>dotnet run -c Release -- --probe-compiled</c>.
/// </summary>
internal static class CompiledSpeedProbe
{
    public static async Task RunAsync()
    {
        const int iterations = 20_000_000;
        const int warmup = 1_000_000;

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
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();
        var plan = await host.PrepareAsync(module, policy);

        // Warm up JIT + compile the plan.
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

    private static long Handwritten(int iterations)
    {
        var total = 0;
        for (var i = 0; i < iterations; i++)
        {
            total = (total + i) % 1_000_003;
        }

        return total;
    }

    private static async Task<SandboxValue?> RunSandbox(SandboxHost host, ExecutionPlan plan, int iterations, ExecutionMode mode)
    {
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = mode });
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
          "id": "compiled-speed-probe",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "iterations", "type": "I32" }],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "total", "value": { "i32": 0 } },
                {
                  "op": "forRange", "local": "i", "start": { "i32": 0 }, "end": { "var": "iterations" },
                  "body": [
                    { "op": "set", "name": "total", "value": {
                      "op": "rem",
                      "left": { "op": "add", "left": { "var": "total" }, "right": { "var": "i" } },
                      "right": { "i32": 1000003 } } }
                  ]
                },
                { "op": "return", "value": { "var": "total" } }
              ]
            }
          ]
        }
        """;
}
