using System.Diagnostics;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class I32MathIntrinsicProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = MeasureBoxed(Warmup);
        _ = MeasureRaw(Warmup);

        var boxed = MeasureBoxed(Iterations);
        var raw = MeasureRaw(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("boxed direct AbsI32", boxed);
        Write("raw AbsI32Raw", raw);
        Console.WriteLine($"saved per call: {(boxed.AllocatedBytes - raw.AllocatedBytes) / (double)Iterations:N1} B");
    }

    private static Measurement MeasureBoxed(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var meter = new ResourceMeter(new ResourceLimits(MaxHostCalls: int.MaxValue, MaxFuel: long.MaxValue));
        var total = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeHostCall("math.abs", maxCallsPerRun: null);
            meter.ChargeFuel(2);
            var value = CompiledRuntime.AbsI32(SandboxValue.FromInt32((i % 101) - 50));
            total = SandboxInt32Math.Add(total, ((I32Value)value).Value);
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
        var total = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            meter.ChargeHostCall("math.abs", maxCallsPerRun: null);
            meter.ChargeFuel(2);
            var value = CompiledRuntime.AbsI32Raw((i % 101) - 50);
            total = SandboxInt32Math.Add(total, value);
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
            $"{name,-22} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.HostCalls,12:N0} calls total={measurement.Total:N0}");

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        int HostCalls,
        int Total);
}
