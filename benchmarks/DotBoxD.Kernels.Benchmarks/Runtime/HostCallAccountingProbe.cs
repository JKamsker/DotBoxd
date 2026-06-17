using System.Diagnostics;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class HostCallAccountingProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = Measure(Warmup, maxCallsPerRun: null);
        _ = Measure(Warmup, maxCallsPerRun: int.MaxValue);

        var unlimited = Measure(Iterations, maxCallsPerRun: null);
        var limited = Measure(Iterations, maxCallsPerRun: int.MaxValue);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("ChargeHostCall unlimited", unlimited);
        Write("ChargeHostCall limited", limited);
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

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.HostCalls,12:N0} calls");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int HostCalls);
}
