using System.Diagnostics;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class RawUnaryNegationProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        _ = MeasureI64BoxedAssign(Warmup);
        _ = MeasureI64RawAssign(Warmup);
        _ = MeasureI64RawReturn(Warmup);
        _ = MeasureF64BoxedAssign(Warmup);
        _ = MeasureF64RawAssign(Warmup);
        _ = MeasureF64RawReturn(Warmup);

        var i64BoxedAssign = MeasureI64BoxedAssign(Iterations);
        var i64RawAssign = MeasureI64RawAssign(Iterations);
        var i64RawReturn = MeasureI64RawReturn(Iterations);
        var f64BoxedAssign = MeasureF64BoxedAssign(Iterations);
        var f64RawAssign = MeasureF64RawAssign(Iterations);
        var f64RawReturn = MeasureF64RawReturn(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("I64 boxed unary assign", i64BoxedAssign);
        Write("I64 raw unary assign", i64RawAssign);
        Write("I64 raw unary return", i64RawReturn);
        Write("F64 boxed unary assign", f64BoxedAssign);
        Write("F64 raw unary assign", f64RawAssign);
        Write("F64 raw unary return", f64RawReturn);
    }

    private static Measurement MeasureI64BoxedAssign(int iterations)
    {
        ForceGc();

        long total = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = CompiledRuntime.AsI64(CompiledRuntime.Neg(CompiledRuntime.I64((i % 101) - 50L)));
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
            var value = CompiledRuntime.NegI64Raw((i % 101) - 50L);
            total ^= value;
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, total);
    }

    private static Measurement MeasureI64RawReturn(int iterations)
    {
        ForceGc();

        long total = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = CompiledRuntime.I64(CompiledRuntime.NegI64Raw((i % 101) - 50L));
            total ^= ((I64Value)value).Value;
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
            total += CompiledRuntime.AsF64(CompiledRuntime.Neg(CompiledRuntime.F64((i % 101) - 50.25)));
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
            total += -((i % 101) - 50.25);
        }

        sw.Stop();
        return new Measurement(sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, total);
    }

    private static Measurement MeasureF64RawReturn(int iterations)
    {
        ForceGc();

        var total = 0.0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = CompiledRuntime.F64(-((i % 101) - 50.25));
            total += ((F64Value)value).Value;
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
