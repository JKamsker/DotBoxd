using System.Diagnostics;
using System.Reflection;
using System.Text;
using DotBoxD.Hosting.Http;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpRequestByteAccountingProbe
{
    private const int Warmup = 10_000;
    private const int Iterations = 1_000_000;
    private static readonly Uri RequestUri = new("https://api.example.com/config?tenant=alpha&mode=full");
    private static readonly Func<Uri, long> ProductionMeasure = CreateMeasureDelegate();

    public static void Run()
    {
        _ = Measure(Warmup, "production request bytes", ProductionMeasure);
        _ = Measure(Warmup, "split request bytes", MeasureSplit);

        var production = Measure(Iterations, "production request bytes", ProductionMeasure);
        var split = Measure(Iterations, "split request bytes", MeasureSplit);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine($"uri = {RequestUri.AbsoluteUri}");
        Write(production);
        Write(split);
    }

    private static Measurement Measure(int iterations, string name, Func<Uri, long> measure)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        long bytes = 0;
        for (var i = 0; i < iterations; i++)
        {
            bytes += measure(RequestUri);
        }

        sw.Stop();
        return new Measurement(
            name,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            bytes);
    }

    private static long MeasureSplit(Uri uri)
        => 4 + Encoding.UTF8.GetByteCount(uri.AbsoluteUri);

    private static Func<Uri, long> CreateMeasureDelegate()
    {
        var type = typeof(SafeHttpClient).Assembly.GetType(
            "DotBoxD.Hosting.Http.Internal.SafeHttpRequestAccounting",
            throwOnError: true)!;
        var method = type.GetMethod(
            "MeasureGetRequestBytes",
            BindingFlags.Public | BindingFlags.Static)!;
        return method.CreateDelegate<Func<Uri, long>>();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-24} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.Bytes,14:N0} request B");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        long Bytes);
}
