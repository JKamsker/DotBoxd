using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Kernels.Runtime;

internal static class ValueShapeCacheProbe
{
    private const int Warmup = 500;
    private const int Iterations = 10_000;

    public static void Run()
    {
        _ = Measure(Warmup);

        var measurement = Measure(Iterations);
        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"CompiledRuntime.ListAdd scalar shape cache {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B");
        Console.WriteLine(
            $"usage: fuel={measurement.FuelUsed:N0}, collectionElements={measurement.CollectionElements:N0}");
    }

    private static Measurement Measure(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext(iterations);
        var value = CompiledRuntime.ListEmpty(context, SandboxType.I32);
        var item = SandboxValue.FromInt32(1);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            value = CompiledRuntime.ListAdd(context, value, item);
        }

        sw.Stop();
        GC.KeepAlive(value);

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            allocated,
            context.Budget.FuelUsed,
            context.Budget.CollectionElements);
    }

    private static SandboxContext CreateContext(int iterations)
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxListLength: iterations,
            MaxTotalCollectionElements: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long FuelUsed,
        long CollectionElements);
}
