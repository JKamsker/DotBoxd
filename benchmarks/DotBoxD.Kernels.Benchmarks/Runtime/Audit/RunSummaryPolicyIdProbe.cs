using System.Diagnostics;
using System.Reflection;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Benchmarks.Runtime.Audit;

internal static class RunSummaryPolicyIdProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;

    private static readonly Func<string?, string> SafePolicyId = CreateSafePolicyIdDelegate();

    public static void Run()
    {
        _ = Measure(Warmup, "summary-policy");
        _ = Measure(Warmup, "  summary-policy\r\n");
        _ = Measure(Warmup, "tenant-prod-api-key=abc123\nnext");

        var clean = Measure(Iterations, "summary-policy");
        var trimmed = Measure(Iterations, "  summary-policy\r\n");
        var secretMarker = Measure(Iterations, "tenant-prod-api-key=abc123\nnext");

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("SafePolicyId clean", clean);
        Write("SafePolicyId trimmed/control", trimmed);
        Write("SafePolicyId secret marker", secretMarker);
    }

    private static Measurement Measure(int iterations, string? policyId)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum += SafePolicyId(policyId).Length;
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum);
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-32} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.Milliseconds * 1_000_000 / Iterations,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.AllocatedBytes / (double)Iterations,8:N1} B/op " +
            $"{measurement.Checksum,12:N0} checksum");

    private static Func<string?, string> CreateSafePolicyIdDelegate()
    {
        var method = typeof(RunSummaryAuditFields).GetMethod(
            "SafePolicyId",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(RunSummaryAuditFields), "SafePolicyId");
        return method.CreateDelegate<Func<string?, string>>();
    }

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        int Checksum);
}
