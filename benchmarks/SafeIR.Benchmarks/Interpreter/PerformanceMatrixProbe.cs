namespace SafeIR.Benchmarks.Interpreter;

using System.Diagnostics;
using SafeIR;
using SafeIR.Hosting;
using SafeIR.Serialization.Json;

internal static class PerformanceMatrixProbe
{
    public static async Task RunAsync()
    {
        var host = SandboxHost.Create(builder =>
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

        Console.WriteLine("case                         handwritten   compiled      x   interpreted      x");
        foreach (var probe in PerformanceMatrixCases.All())
        {
            await RunCase(host, policy, probe);
        }
    }

    private static async Task RunCase(SandboxHost host, SandboxPolicy policy, PerformanceMatrixCase probe)
    {
        var module = await host.ImportJsonAsync(probe.ModuleJson);
        var plan = await host.PrepareAsync(module, policy);

        _ = probe.Handwritten(probe.Warmup);
        _ = await RunSandbox(host, plan, probe.Warmup, ExecutionMode.Compiled);
        _ = await RunSandbox(host, plan, probe.Warmup, ExecutionMode.Interpreted);

        var handwrittenMs = Time(() => probe.Handwritten(probe.Iterations));
        var compiledMs = await TimeAsync(() => RunSandbox(host, plan, probe.Iterations, ExecutionMode.Compiled));
        var interpretedMs = await TimeAsync(() => RunSandbox(host, plan, probe.Iterations, ExecutionMode.Interpreted));

        Console.WriteLine(
            $"{probe.Name,-28} {handwrittenMs,8:N1} ms {compiledMs,8:N1} ms {compiledMs / handwrittenMs,5:N1} {interpretedMs,10:N1} ms {interpretedMs / handwrittenMs,6:N1}");
    }

    private static async Task<SandboxValue?> RunSandbox(SandboxHost host, ExecutionPlan plan, int iterations, ExecutionMode mode)
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

    private static double Time(Func<object> action)
    {
        var sw = Stopwatch.StartNew();
        GC.KeepAlive(action());
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
}
