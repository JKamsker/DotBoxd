namespace DotBoxD.Kernels.Benchmarks.Runtime;

using System.Diagnostics;
using DotBoxD.Plugins;

internal static class KernelRpcValueListWriterProbe
{
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var values = new List<int> { 4, 5, 6, 7 };
        var empty = new List<int>();
        var emptyMap = new Dictionary<string, int>();
        var legacy = Measure("List writer via List<T> + ToArray", values, useLegacy: true);
        var direct = MeasureDirect("List writer via direct array fill", values, useEmptyFastPath: false);
        var emptyIndexedLegacy = MeasureDirect(
            "Empty indexed writer via zero array",
            empty,
            useEmptyFastPath: false);
        var emptyIndexedCurrent = MeasureDirect(
            "Empty indexed writer via Array.Empty",
            empty,
            useEmptyFastPath: true);
        IEnumerable<int> enumerable = values;
        IEnumerable<int> emptyEnumerable = empty;
        var enumerableFallback = MeasureEnumerable(
            "IEnumerable writer via List<T> + ToArray",
            enumerable,
            useCountedBranch: false,
            useEmptyFastPath: false);
        var enumerableCounted = MeasureEnumerable(
            "IEnumerable writer via counted array fill",
            enumerable,
            useCountedBranch: true,
            useEmptyFastPath: false);
        var emptyEnumerableLegacy = MeasureEnumerable(
            "Empty counted IEnumerable via zero array",
            emptyEnumerable,
            useCountedBranch: true,
            useEmptyFastPath: false);
        var emptyEnumerableCurrent = MeasureEnumerable(
            "Empty counted IEnumerable via Array.Empty",
            emptyEnumerable,
            useCountedBranch: true,
            useEmptyFastPath: true);
        var emptyMapLegacy = MeasureMap(
            "Empty map writer via zero array",
            emptyMap,
            useEmptyFastPath: false);
        var emptyMapCurrent = MeasureMap(
            "Empty map writer via Array.Empty",
            emptyMap,
            useEmptyFastPath: true);

        Write(legacy);
        Write(direct);
        Write(emptyIndexedLegacy);
        Write(emptyIndexedCurrent);
        Write(enumerableFallback);
        Write(enumerableCounted);
        Write(emptyEnumerableLegacy);
        Write(emptyEnumerableCurrent);
        Write(emptyMapLegacy);
        Write(emptyMapCurrent);
    }

    private static Measurement Measure(string name, List<int> values, bool useLegacy)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = useLegacy ? WriteLegacy(values) : WriteDirect(values, useEmptyFastPath: false);
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return new Measurement(name, watch.Elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static Measurement MeasureDirect(string name, List<int> values, bool useEmptyFastPath)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = WriteDirect(values, useEmptyFastPath);
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return new Measurement(name, watch.Elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static Measurement MeasureEnumerable(
        string name,
        IEnumerable<int> values,
        bool useCountedBranch,
        bool useEmptyFastPath)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = useCountedBranch
            ? WriteCountedEnumerable(values, useEmptyFastPath)
            : WriteEnumerableFallback(values);
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(checksum);
        return new Measurement(name, watch.Elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static Measurement MeasureMap(string name, Dictionary<string, int> values, bool useEmptyFastPath)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var checksum = WriteMap(values, useEmptyFastPath);
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

    private static long WriteCountedEnumerable(IEnumerable<int> values, bool useEmptyFastPath)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            if (!Enumerable.TryGetNonEnumeratedCount(values, out var count))
            {
                checksum += WriteSingleEnumerableFallback(values);
                continue;
            }

            var items = useEmptyFastPath && count == 0
                ? Array.Empty<KernelRpcValue>()
                : new KernelRpcValue[count];
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

    private static long WriteDirect(List<int> values, bool useEmptyFastPath)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var items = useEmptyFastPath && values.Count == 0
                ? Array.Empty<KernelRpcValue>()
                : new KernelRpcValue[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                items[i] = KernelRpcValue.Int32(values[i]);
            }

            checksum += KernelRpcValue.List(items).ItemCount;
        }

        return checksum;
    }

    private static long WriteMap(Dictionary<string, int> values, bool useEmptyFastPath)
    {
        long checksum = 0;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var entryCount = values.Count * 2;
            var entries = useEmptyFastPath && entryCount == 0
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
