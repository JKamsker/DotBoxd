namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Plugins;

internal static class KernelRpcValueListWriterProbe
{
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var values = new List<int> { 4, 5, 6, 7 };
        var legacy = Measure("List writer via List<T> + ToArray", values, useLegacy: true);
        var direct = Measure("List writer via direct array fill", values, useLegacy: false);
        IEnumerable<int> enumerable = values;
        var enumerableFallback = MeasureEnumerable(
            "IEnumerable writer via List<T> + ToArray",
            enumerable,
            useCountedBranch: false);
        var enumerableCounted = MeasureEnumerable(
            "IEnumerable writer via counted array fill",
            enumerable,
            useCountedBranch: true);

        Write(legacy);
        Write(direct);
        Write(enumerableFallback);
        Write(enumerableCounted);
    }

    private static Measurement Measure(string name, List<int> values, bool useLegacy)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = useLegacy ? WriteLegacy(values) : WriteDirect(values);
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return new Measurement(name, watch.Elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static Measurement MeasureEnumerable(string name, IEnumerable<int> values, bool useCountedBranch)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = useCountedBranch ? WriteCountedEnumerable(values) : WriteEnumerableFallback(values);
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return new Measurement(name, watch.Elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static long WriteLegacy(List<int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var items = new List<KernelRpcValue>();
            foreach (var item in values)
            {
                items.Add(KernelRpcValue.Int32(item));
            }

            checksum += KernelRpcValue.List(items.ToArray()).ItemCount;
        }

        return checksum;
    }

    private static long WriteEnumerableFallback(IEnumerable<int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += WriteSingleEnumerableFallback(values);
        }

        return checksum;
    }

    private static long WriteCountedEnumerable(IEnumerable<int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            if (!Enumerable.TryGetNonEnumeratedCount(values, out var count))
            {
                checksum += WriteSingleEnumerableFallback(values);
                continue;
            }

            var items = new KernelRpcValue[count];
            var index = 0;
            foreach (var item in values)
            {
                if (index >= items.Length)
                {
                    Array.Resize(ref items, checked(index + 1));
                }

                items[index++] = KernelRpcValue.Int32(item);
            }

            if (index != items.Length)
            {
                Array.Resize(ref items, index);
            }

            checksum += KernelRpcValue.List(items).ItemCount;
        }

        return checksum;
    }

    private static int WriteSingleEnumerableFallback(IEnumerable<int> values)
    {
        var items = new List<KernelRpcValue>();
        foreach (var item in values)
        {
            items.Add(KernelRpcValue.Int32(item));
        }

        return KernelRpcValue.List(items.ToArray()).ItemCount;
    }

    private static long WriteDirect(List<int> values)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var items = new KernelRpcValue[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                items[i] = KernelRpcValue.Int32(values[i]);
            }

            checksum += KernelRpcValue.List(items).ItemCount;
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
