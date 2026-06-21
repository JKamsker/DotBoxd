using System.Diagnostics;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class HostCallAccountingProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = Measure(Warmup, maxCallsPerRun: null);
        _ = Measure(Warmup, maxCallsPerRun: int.MaxValue);
        _ = MeasureAlternatingLimited(Warmup);

        var unlimited = Measure(Iterations, maxCallsPerRun: null);
        var limited = Measure(Iterations, maxCallsPerRun: int.MaxValue);
        var alternatingLimited = MeasureAlternatingLimited(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("ChargeHostCall unlimited", unlimited);
        Write("ChargeHostCall limited", limited);
        Write("ChargeHostCall limited alternating", alternatingLimited);
    }

    private static Measurement Measure(int iterations, int? maxCallsPerRun)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var limits = new ResourceLimits(MaxHostCalls: int.MaxValue);
        var meter = new ResourceMeter(limits);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeHostCall("probe.binding", maxCallsPerRun);
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            meter.HostCalls);
    }

    private static Measurement MeasureAlternatingLimited(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var limits = new ResourceLimits(MaxHostCalls: int.MaxValue);
        var meter = new ResourceMeter(limits);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeHostCall((i & 1) == 0 ? "probe.binding.a" : "probe.binding.b", int.MaxValue);
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            meter.HostCalls);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.HostCalls,12:N0} calls");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int HostCalls);
}
