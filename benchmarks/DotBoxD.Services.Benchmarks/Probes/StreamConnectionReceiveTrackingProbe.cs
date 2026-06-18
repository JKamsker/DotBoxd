using System.Diagnostics;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class StreamConnectionReceiveTrackingProbe
{
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var legacy = Measure(Iterations, simulateLegacyTracking: true);
        var current = Measure(Iterations, simulateLegacyTracking: false);

        Console.WriteLine("StreamConnection receive tracking probe");
        Write("Owned receive with legacy tracking", legacy);
        Write("Owned receive current", current);
    }

    private static Measurement Measure(int iterations, bool simulateLegacyTracking)
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        var connection = new StreamConnection(stream, ownsStream: true);
        var activeReceives = 0;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (simulateLegacyTracking)
            {
                Interlocked.Increment(ref activeReceives);
            }

            try
            {
                var payload = connection.ReceiveAsync().GetAwaiter().GetResult();
                if (!ReferenceEquals(payload, Payload.Empty))
                {
                    payload.Dispose();
                }
            }
            finally
            {
                if (simulateLegacyTracking)
                {
                    Interlocked.Decrement(ref activeReceives);
                }
            }
        }

        sw.Stop();
        connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new Measurement(
            iterations,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static void Write(string name, Measurement measurement)
    {
        Console.WriteLine(
            $"{name,-35} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,12:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op");
    }

    private readonly record struct Measurement(
        int Iterations,
        double Milliseconds,
        long AllocatedBytes)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
