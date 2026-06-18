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

        Write(legacy);
        Write(direct);
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
