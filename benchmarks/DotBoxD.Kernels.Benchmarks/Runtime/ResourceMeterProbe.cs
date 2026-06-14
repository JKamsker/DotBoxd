namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Kernels;

internal static class ResourceMeterProbe
{
    public static void Run()
    {
        const int iterations = 1_000_000;
        const int warmup = 50_000;
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxAllocatedBytes: long.MaxValue,
            MaxTotalCollectionElements: long.MaxValue,
            MaxTotalStringBytes: long.MaxValue);
        var pluginInput = SandboxValue.FromList(
            [
                SandboxValue.FromString("fire"),
                SandboxValue.FromInt32(120),
                SandboxValue.FromString("player-1"),
                SandboxValue.FromString("fire"),
                SandboxValue.FromInt32(100)
            ],
            SandboxType.String);

        _ = Measure(warmup, limits, pluginInput);

        var measurement = Measure(iterations, limits, pluginInput);
        Console.WriteLine($"iterations = {iterations:N0}");
        Console.WriteLine($"ChargeValue(plugin flat scalar input) {measurement.Milliseconds,8:N1} ms {measurement.AllocatedBytes,14:N0} B");
        Console.WriteLine($"usage: collectionElements={measurement.CollectionElements:N0}, stringBytes={measurement.StringBytes:N0}");
    }

    private static Measurement Measure(int iterations, ResourceLimits limits, SandboxValue value)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var meter = new ResourceMeter(limits);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeValue(value);
        }

        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            allocated,
            meter.CollectionElements,
            meter.StringBytes);
    }

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long CollectionElements,
        long StringBytes);
}
