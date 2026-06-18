namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Plugins;

internal static class KernelRpcValueItemsProbe
{
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var value = KernelRpcValue.Record(
        [
            KernelRpcValue.Int32(4),
            KernelRpcValue.Int32(5),
            KernelRpcValue.Int32(6),
            KernelRpcValue.Int32(7)
        ]);

        var cloned = Measure("Items clone reader", value, useClone: true);
        var indexed = Measure("ItemCount/GetItem reader", value, useClone: false);

        Write(cloned);
        Write(indexed);
    }

    private static Measurement Measure(string name, KernelRpcValue value, bool useClone)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var sum = useClone ? SumCloned(value) : SumIndexed(value);
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sum);
        return new Measurement(name, watch.Elapsed.TotalMilliseconds, allocated, sum);
    }

    private static long SumCloned(KernelRpcValue value)
    {
        long sum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var fields = value.Items;
            for (var i = 0; i < fields.Length; i++)
            {
                sum += fields[i].Int32Value;
            }
        }

        return sum;
    }

    private static long SumIndexed(KernelRpcValue value)
    {
        long sum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var count = value.ItemCount;
            for (var i = 0; i < count; i++)
            {
                sum += value.GetItem(i).Int32Value;
            }
        }

        return sum;
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name}: {measurement.Milliseconds:N1} ms, " +
            $"{measurement.AllocatedBytes:N0} B, checksum={measurement.Sum:N0}");

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        long Sum);
}
