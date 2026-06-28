using System.Diagnostics;
using DotBoxD.Kernels.Serialization.Json.Schema;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Benchmarks.Json;

internal static class JsonSchemaResourceProbe
{
    private const int Warmup = 1_000;
    private const int Iterations = 20_000;

    public static void Run()
    {
        _ = Measure("module schema", Warmup, static () => JsonSchemas.ModuleEnvelope);
        _ = Measure("plugin schema", Warmup, static () => PluginPackageJsonSchemas.PackageEnvelope);

        var module = Measure("module schema", Iterations, static () => JsonSchemas.ModuleEnvelope);
        var plugin = Measure(
            "plugin schema",
            Iterations,
            static () => PluginPackageJsonSchemas.PackageEnvelope);
        var alternating = MeasureAlternating(Iterations);

        Console.WriteLine("JSON schema resource probe");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Print(module);
        Print(plugin);
        Print(alternating);
    }

    private static Measurement Measure(string name, int iterations, Func<string> load)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum += load().Length;
        }

        watch.Stop();
        return Measurement.Create(
            name,
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static Measurement MeasureAlternating(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum += (i & 1) == 0
                ? JsonSchemas.ModuleEnvelope.Length
                : PluginPackageJsonSchemas.PackageEnvelope.Length;
        }

        watch.Stop();
        return Measurement.Create(
            "alternating schemas",
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static void Print(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-20} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op " +
            $"{measurement.Checksum,12:N0} checksum");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        double NanosecondsPerOperation,
        long AllocatedBytes,
        double BytesPerOperation,
        int Checksum)
    {
        public static Measurement Create(
            string name,
            double milliseconds,
            long allocatedBytes,
            int checksum,
            int iterations)
            => new(
                name,
                milliseconds,
                milliseconds * 1_000_000 / iterations,
                allocatedBytes,
                (double)allocatedBytes / iterations,
                checksum);
    }
}
