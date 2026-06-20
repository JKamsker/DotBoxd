using System.Diagnostics;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class NumericConversionProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = MeasureI64BoxedAssign(Warmup);
        _ = MeasureI64RawAssign(Warmup);
        _ = MeasureF64BoxedAssign(Warmup);
        _ = MeasureF64RawAssign(Warmup);

        var i64Boxed = MeasureI64BoxedAssign(Iterations);
        var i64Raw = MeasureI64RawAssign(Iterations);
        var f64Boxed = MeasureF64BoxedAssign(Iterations);
        var f64Raw = MeasureF64RawAssign(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("I32->I64 boxed assign", i64Boxed);
        Write("I32->I64 raw assign", i64Raw);
        Write("I64->F64 boxed assign", f64Boxed);
        Write("I64->F64 raw assign", f64Raw);
    }

    private static Measurement MeasureI64BoxedAssign(int iterations)
    {
        ForceGc();

        long total = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = CompiledRuntime.AsI64(CompiledRuntime.I64((i % 101) - 50));
            total ^= value;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, total);
    }

    private static Measurement MeasureI64RawAssign(int iterations)
    {
        ForceGc();

        long total = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = (long)((i % 101) - 50);
            total ^= value;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, total);
    }

    private static Measurement MeasureF64BoxedAssign(int iterations)
    {
        ForceGc();

        var total = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = CompiledRuntime.AsF64(CompiledRuntime.F64((i % 101) - 50L));
            total += value;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, total);
    }

    private static Measurement MeasureF64RawAssign(int iterations)
    {
        ForceGc();

        var total = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = (double)((i % 101) - 50L);
            total += value;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, total);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-24} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B total={measurement.Total:N0}");

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, double Total);
}
