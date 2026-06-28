using System.Diagnostics;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Benchmarks.Interpreter;

internal static class BranchedF64LoopProbe
{
    private const int WarmupIterations = 100_000;
    private const int Iterations = 5_000_000;
    private const int Samples = 7;

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
            .WithMaxHostCalls(int.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

        var module = await host.ImportJsonAsync(PerformanceMatrixControlFlowCases.BranchedF64LoopJson());
        var plan = await host.PrepareAsync(module, policy);

        _ = await RunSandbox(host, plan, WarmupIterations);
        var samples = new double[Samples];
        for (var i = 0; i < samples.Length; i++)
        {
            ForceGc();
            samples[i] = await TimeAsync(() => RunSandbox(host, plan, Iterations));
        }

        Array.Sort(samples);
        Console.WriteLine($"branched f64 interpreted loop iterations = {Iterations:N0}");
        Console.WriteLine($"samples = {Samples:N0}");
        Console.WriteLine($"min = {samples[0]:N1} ms");
        Console.WriteLine($"median = {samples[samples.Length / 2]:N1} ms");
        Console.WriteLine($"max = {samples[^1]:N1} ms");
    }

    private static async Task<SandboxValue?> RunSandbox(Hosting.Execution.SandboxHost host, ExecutionPlan plan, int iterations)
    {
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.FromInt32(iterations),
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted, AllowFallbackToInterpreter = false });
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error?.SafeMessage ?? "execution failed");
        }

        return result.Value;
    }

    private static async Task<double> TimeAsync(Func<Task<SandboxValue?>> action)
    {
        var sw = Stopwatch.StartNew();
        GC.KeepAlive(await action());
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
