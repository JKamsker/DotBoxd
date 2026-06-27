using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;

internal static class ResourceMeterProbe
{
    public static void Run()
    {
        const int flatIterations = 1_000_000;
        const int nestedIterations = 200_000;
        const int warmup = 20_000;
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
        var nestedInput = CreateNestedValue();

        _ = Measure(warmup, limits, pluginInput);
        _ = Measure(warmup, limits, nestedInput);

        var flat = Measure(flatIterations, limits, pluginInput);
        var nested = Measure(nestedIterations, limits, nestedInput);
        Console.WriteLine($"flat iterations = {flatIterations:N0}");
        Write("ChargeValue(plugin flat scalar input)", flat, flatIterations);
        Console.WriteLine($"nested iterations = {nestedIterations:N0}");
        Write("ChargeValue(nested structural input)", nested, nestedIterations);
    }

    private static SandboxValue CreateNestedValue()
        => SandboxValue.FromRecord([
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1),
                    [SandboxValue.FromString("two")] = SandboxValue.FromInt32(2)
                },
                SandboxType.String,
                SandboxType.I32),
            SandboxValue.FromList(
                [
                    SandboxValue.FromRecord([
                        SandboxValue.FromInt32(7),
                        SandboxValue.FromString("alpha"),
                        SandboxValue.FromMap(
                            new Dictionary<SandboxValue, SandboxValue>
                            {
                                [SandboxValue.FromString("score")] = SandboxValue.FromInt64(42)
                            },
                            SandboxType.String,
                            SandboxType.I64)
                    ])
                ],
                SandboxType.Record([
                    SandboxType.I32,
                    SandboxType.String,
                    SandboxType.Map(SandboxType.String, SandboxType.I64)
                ]))
        ]);

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

    private static void Write(string name, Measurement measurement, int iterations)
        => Console.WriteLine(
            $"{name,-39} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.Milliseconds * 1_000_000 / iterations,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{(double)measurement.AllocatedBytes / iterations,8:N1} B/op " +
            $"collectionElements={measurement.CollectionElements:N0}, stringBytes={measurement.StringBytes:N0}");

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long CollectionElements,
        long StringBytes);
}
