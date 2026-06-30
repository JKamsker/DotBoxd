namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Plugins;

internal static class KernelRpcBinaryCodecEmptyDecodeProbe
{
    private const int Iterations = 1_000_000;
    private static object? s_sink;

    public static void Run()
    {
        var emptyArguments = KernelRpcBinaryCodec.EncodeArguments(Array.Empty<KernelRpcValue>());
        var emptyList = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(Array.Empty<KernelRpcValue>()));
        var emptyRecord = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Record(Array.Empty<KernelRpcValue>()));
        var emptyMap = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map(Array.Empty<KernelRpcValue>()));

        Write(Measure("legacy empty arguments decode", () => DecodeLegacyEmptyArguments(emptyArguments)));
        Write(Measure("current empty arguments decode", () => DecodeCurrentEmptyArguments(emptyArguments)));
        Write(Measure("legacy empty list decode", () => DecodeLegacyEmptyValue(emptyList)));
        Write(Measure("current empty list decode", () => DecodeCurrentEmptyValue(emptyList)));
        Write(Measure("legacy empty record decode", () => DecodeLegacyEmptyValue(emptyRecord)));
        Write(Measure("current empty record decode", () => DecodeCurrentEmptyValue(emptyRecord)));
        Write(Measure("legacy empty map decode", () => DecodeLegacyEmptyValue(emptyMap)));
        Write(Measure("current empty map decode", () => DecodeCurrentEmptyValue(emptyMap)));
    }

    private static Measurement Measure(string name, Func<long> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = action();
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return new Measurement(name, watch.Elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static long DecodeLegacyEmptyArguments(byte[] payload)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            RequireEmptyLengthPayload(payload);
            var values = LegacyEmptyArray();
            s_sink = values;
            checksum += values.Length;
        }

        return checksum;
    }

    private static long DecodeCurrentEmptyArguments(byte[] payload)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var values = KernelRpcBinaryCodec.DecodeArguments(payload);
            s_sink = values;
            checksum += values.Length;
        }

        return checksum;
    }

    private static long DecodeLegacyEmptyValue(byte[] payload)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            RequireEmptyItemPayload(payload);
            var items = LegacyEmptyArray();
            s_sink = items;
            var value = payload[0] switch
            {
                (byte)KernelRpcValueKind.List => KernelRpcValue.List(items),
                (byte)KernelRpcValueKind.Record => KernelRpcValue.Record(items),
                (byte)KernelRpcValueKind.Map => KernelRpcValue.Map(items),
                _ => throw new InvalidOperationException("Probe payload must be an empty collection value.")
            };
            checksum += value.ItemCount;
        }

        return checksum;
    }

    private static long DecodeCurrentEmptyValue(byte[] payload)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += KernelRpcBinaryCodec.DecodeValue(payload).ItemCount;
        }

        return checksum;
    }

    private static void RequireEmptyLengthPayload(byte[] payload)
    {
        if (payload.Length != 1 || payload[0] != 0)
        {
            throw new InvalidOperationException("Probe payload must encode an empty argument list.");
        }
    }

    private static void RequireEmptyItemPayload(byte[] payload)
    {
        if (payload.Length != 2 || payload[1] != 0)
        {
            throw new InvalidOperationException("Probe payload must encode an empty collection value.");
        }
    }

    private static KernelRpcValue[] LegacyEmptyArray()
    {
#pragma warning disable MA0005 // Intentional legacy allocation measured by this probe.
        return new KernelRpcValue[0];
#pragma warning restore MA0005
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name}: {measurement.Milliseconds:N1} ms, " +
            $"{measurement.AllocatedBytes:N0} B, checksum={measurement.Checksum:N0}");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        long Checksum);
}
