using System.Diagnostics;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class F64MathIntrinsicProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = MeasureBoxed(Warmup);
        _ = MeasureRaw(Warmup);
        _ = MeasureRawReturn(Warmup);

        var boxed = MeasureBoxed(Iterations);
        var raw = MeasureRaw(Iterations);
        var rawReturn = MeasureRawReturn(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("boxed direct FloorF64", boxed);
        Write("raw FloorF64Raw", raw);
        Write("raw FloorF64Raw + box", rawReturn);
        Console.WriteLine($"saved per call: {(boxed.AllocatedBytes - raw.AllocatedBytes) / (double)Iterations:N1} B");
    }

    private static Measurement MeasureBoxed(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: int.MaxValue, MaxFuel: long.MaxValue));
        var total = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeHostCall("math.floor", maxCallsPerRun: null);
            meter.ChargeFuel(2);
            var value = CompiledRuntime.FloorF64(SandboxValue.FromDouble((i % 101) + 0.75));
            total += ((F64Value)value).Value;
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            meter.HostCalls,
            total);
    }

    private static Measurement MeasureRaw(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: int.MaxValue, MaxFuel: long.MaxValue));
        var total = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeHostCall("math.floor", maxCallsPerRun: null);
            meter.ChargeFuel(2);
            total += CompiledRuntime.FloorF64Raw((i % 101) + 0.75);
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            meter.HostCalls,
            total);
    }

    private static Measurement MeasureRawReturn(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: int.MaxValue, MaxFuel: long.MaxValue));
        var total = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeHostCall("math.floor", maxCallsPerRun: null);
            meter.ChargeFuel(2);
            var value = CompiledRuntime.F64(CompiledRuntime.FloorF64Raw((i % 101) + 0.75));
            total += ((F64Value)value).Value;
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            meter.HostCalls,
            total);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-23} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.HostCalls,12:N0} calls total={measurement.Total:N0}");

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        int HostCalls,
        double Total);
}
