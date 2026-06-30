namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Plugins;

internal static class InvokeAsyncCaptureArgumentWriterProbe
{
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var values = new List<int> { 4, 5, 6, 7 };
        var scores = new Dictionary<string, int>
        {
            ["fire"] = 4,
            ["ice"] = 5,
            ["arcane"] = 6,
            ["shadow"] = 7
        };

        Write(Measure("InvokeAsync list capture via LINQ Select+ToArray", () => WriteLegacyList(values)));
        Write(Measure("InvokeAsync list capture via direct array fill", () => WriteDirectList(values)));
        Write(Measure("InvokeAsync map capture via LINQ SelectMany+ToArray", () => WriteLegacyMap(scores)));
        Write(Measure("InvokeAsync map capture via direct array fill", () => WriteDirectMap(scores)));
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

    private static long WriteLegacyList(List<int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += KernelRpcValue.List(
                Enumerable.ToArray(
                    Enumerable.Select(values, static item => KernelRpcValue.Int32(item)))).ItemCount;
        }

        return checksum;
    }

    private static long WriteDirectList(List<int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var count = values.Count;
            var items = count == 0
                ? Array.Empty<KernelRpcValue>()
                : new KernelRpcValue[count];
            for (var index = 0; index < count; index++)
            {
                items[index] = KernelRpcValue.Int32(values[index]);
            }

            checksum += KernelRpcValue.List(items).ItemCount;
        }

        return checksum;
    }

    private static long WriteLegacyMap(Dictionary<string, int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += KernelRpcValue.Map(
                Enumerable.ToArray(
                    Enumerable.SelectMany(
                        values,
                        static pair => new[]
                        {
                            KernelRpcValue.String(pair.Key),
                            KernelRpcValue.Int32(pair.Value)
                        }))).ItemCount;
        }

        return checksum;
    }

    private static long WriteDirectMap(Dictionary<string, int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var entryCount = values.Count * 2;
            var entries = entryCount == 0
                ? Array.Empty<KernelRpcValue>()
                : new KernelRpcValue[entryCount];
            var index = 0;
            foreach (var pair in values)
            {
                entries[index++] = KernelRpcValue.String(pair.Key);
                entries[index++] = KernelRpcValue.Int32(pair.Value);
            }

            checksum += KernelRpcValue.Map(entries).ItemCount;
        }

        return checksum;
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
