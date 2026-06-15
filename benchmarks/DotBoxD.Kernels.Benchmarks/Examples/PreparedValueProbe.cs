namespace DotBoxD.Kernels.Benchmarks.Examples;

using System.Diagnostics;
using DotBoxD.Kernels;
using DotBoxD.Plugins;

internal static class PreparedValueProbe
{
    public static async Task RunAsync()
    {
        const int warmup = 2_000;
        const int iterations = 200_000;
        var e = new ExampleWorkflowProbe.DamageEvent("ice", 120, "player-1");
        var summary = await MeasureShouldHandleMissAsync(warmup, iterations, e);

        Console.WriteLine("case                         iterations   elapsed       allocated      handled");
        Console.WriteLine(
            $"compiled no-audit miss {iterations,14:N0} {summary.Milliseconds,8:N1} ms {summary.AllocatedBytes,13:N0} B {summary.Handled,10:N0}");
    }

    private static async Task<RunSummary> MeasureShouldHandleMissAsync(
        int warmup,
        int iterations,
        ExampleWorkflowProbe.DamageEvent e)
    {
        using var server = PluginServer.Create(
            new InMemoryPluginMessageSink(),
            defaultPolicy: MessageWritePolicy(),
            executionMode: ExecutionMode.Compiled);
        var kernel = await server.InstallJsonAsync(ExampleWorkflowProbe.FireDamagePackageJson());
        var adapter = ExampleWorkflowProbe.DamageEventAdapter.Instance;

        for (var i = 0; i < warmup; i++)
        {
            _ = await kernel.ShouldHandleAsync(adapter, e);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var handled = 0;
        for (var i = 0; i < iterations; i++)
        {
            if (await kernel.ShouldHandleAsync(adapter, e))
            {
                handled++;
            }
        }

        sw.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new RunSummary(sw.Elapsed.TotalMilliseconds, allocatedBytes, handled);
    }

    private static SandboxPolicy MessageWritePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(long.MaxValue)
            .WithMaxHostCalls(int.MaxValue)
            .WithMaxLoopIterations(long.MaxValue)
            .WithMaxTotalStringBytes(long.MaxValue)
            .WithWallTime(TimeSpan.FromMinutes(5))
            .Build();

    private readonly record struct RunSummary(double Milliseconds, long AllocatedBytes, int Handled);
}
