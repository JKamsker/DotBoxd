using System.Diagnostics;
using DotBoxD.Services.Generated;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class GeneratedMetadataParameterArrayProbe
{
    private const int Warmup = 50_000;
    private const int Iterations = 1_000_000;
    private static readonly GeneratedParameter Parameter =
        new("value", typeof(int), 0, IsCancellationToken: false, HasDefaultValue: false, DefaultValue: null);

    private static IReadOnlyList<GeneratedParameter>? s_last;
    private static int s_observedLength;

    public static void Run()
    {
        _ = MeasureLegacyZeroParameters(Warmup);
        _ = MeasureCurrentZeroParameters(Warmup);
        _ = MeasureOneParameterControl(Warmup);

        var legacyZero = MeasureLegacyZeroParameters(Iterations);
        var currentZero = MeasureCurrentZeroParameters(Iterations);
        var oneParameter = MeasureOneParameterControl(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Write("legacy zero metadata parameters", legacyZero);
        Write("current zero metadata parameters", currentZero);
        Write("one metadata parameter control", oneParameter);
    }

#pragma warning disable MA0005 // Intentional legacy baseline: measure the removed zero-length array allocation.
    private static Measurement MeasureLegacyZeroParameters(int iterations)
        => Measure(iterations, static () => new GeneratedParameter[0]);
#pragma warning restore MA0005

    private static Measurement MeasureCurrentZeroParameters(int iterations)
        => Measure(iterations, static () => Array.Empty<GeneratedParameter>());

    private static Measurement MeasureOneParameterControl(int iterations)
        => Measure(iterations, static () => new[] { Parameter });

    private static Measurement Measure(
        int iterations,
        Func<IReadOnlyList<GeneratedParameter>> createParameters)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var observedLength = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var parameters = createParameters();
            observedLength += parameters.Count;
            s_last = parameters;
        }

        sw.Stop();
        s_observedLength = observedLength;
        return new Measurement(
            iterations,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            observedLength);
    }

    private static void Write(string name, Measurement measurement)
    {
        Console.WriteLine(
            $"{name,-34} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,12:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op " +
            $"{measurement.ObservedLength,10:N0} observed");
    }

    private readonly record struct Measurement(
        int Iterations,
        double Milliseconds,
        long AllocatedBytes,
        int ObservedLength)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
