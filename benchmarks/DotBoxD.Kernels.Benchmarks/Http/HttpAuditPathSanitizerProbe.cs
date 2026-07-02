using System.Diagnostics;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Benchmarks.Http;

internal static class HttpAuditPathSanitizerProbe
{
    private const int Warmup = 10_000;
    private const int Iterations = 1_000_000;

    private static readonly Case[] Cases =
    [
        new("clean short", "/config"),
        new("clean multi", "/v1/config/public/status"),
        new("direct secret", "/v1/token/abc123/status"),
        new("encoded secret", "/v1/%74%6f%6b%65%6e/abc123/status"),
    ];

    public static void Run()
    {
        foreach (var scenario in Cases)
        {
            _ = Measure(Warmup, scenario);
        }

        Console.WriteLine($"iterations = {Iterations:N0}");
        foreach (var scenario in Cases)
        {
            Write(Measure(Iterations, scenario));
        }
    }

    private static Measurement Measure(int iterations, Case scenario)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var checksum = 0;
        for (var i = 0; i < iterations; i++)
        {
            checksum += AuditTextSanitizer.RedactPathSegments(scenario.Path).Length;
        }

        sw.Stop();
        return new Measurement(
            scenario.Name,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-14} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op " +
            $"{measurement.Checksum,12:N0} checksum");

    private readonly record struct Case(string Name, string Path);

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        int Checksum)
    {
        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
