using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class KernelRpcMarshallerCollectionsProbe
{
    private const int Warmup = 2_000;
    private const int Iterations = 100_000;

    public static void Run()
    {
        Console.WriteLine("Kernel RPC marshaller collection probe");
        Console.WriteLine($"iterations = {Iterations:N0}");
        RunMapLane(0);
        RunMapLane(4);
        RunMapLane(32);
    }

    private static void RunMapLane(int entries)
    {
        var wireMap = CreateWireMap(entries);
        var source = CreateDictionary(entries);
        var expectedType = SandboxType.Map(SandboxType.String, SandboxType.I32);

        _ = Measure(Warmup, static state =>
        {
            var map = (MapValue)KernelRpcValueConverter.ToSandboxValue(state.WireMap, state.ExpectedType);
            return map.Values.Count;
        }, new WireState(wireMap, expectedType));
        _ = Measure(Warmup, static state =>
        {
            var map = (MapValue)KernelRpcMarshaller.ToSandboxValue(state.Source, typeof(Dictionary<string, int>));
            return map.Values.Count;
        }, new ObjectState(source));

        var wire = Measure(Iterations, static state =>
        {
            var map = (MapValue)KernelRpcValueConverter.ToSandboxValue(state.WireMap, state.ExpectedType);
            return map.Values.Count;
        }, new WireState(wireMap, expectedType));
        var runtime = Measure(Iterations, static state =>
        {
            var map = (MapValue)KernelRpcMarshaller.ToSandboxValue(state.Source, typeof(Dictionary<string, int>));
            return map.Values.Count;
        }, new ObjectState(source));

        Print($"wire map -> sandbox ({entries,2})", wire);
        Print($"object dictionary -> sandbox ({entries,2})", runtime);
    }

    private static KernelRpcValue CreateWireMap(int entries)
    {
        var items = new KernelRpcValue[entries * 2];
        for (var i = 0; i < entries; i++)
        {
            items[i * 2] = KernelRpcValue.String($"key-{i}");
            items[(i * 2) + 1] = KernelRpcValue.Int32(i);
        }

        return KernelRpcValue.Map(items);
    }

    private static Dictionary<string, int> CreateDictionary(int entries)
    {
        var source = new Dictionary<string, int>(entries);
        for (var i = 0; i < entries; i++)
        {
            source[$"key-{i}"] = i;
        }

        return source;
    }

    private static Measurement Measure<TState>(int iterations, Func<TState, int> action, TState state)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            checksum += action(state);
        }

        watch.Stop();
        return Measurement.Create(
            watch.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-34} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op " +
            $"{measurement.Checksum,10:N0} checksum");

    private sealed record WireState(KernelRpcValue WireMap, SandboxType ExpectedType);

    private sealed record ObjectState(Dictionary<string, int> Source);

    private readonly record struct Measurement(
        double Milliseconds,
        double NanosecondsPerOperation,
        long AllocatedBytes,
        double BytesPerOperation,
        int Checksum)
    {
        public static Measurement Create(
            double milliseconds,
            long allocatedBytes,
            int checksum,
            int iterations)
            => new(
                milliseconds,
                milliseconds * 1_000_000 / iterations,
                allocatedBytes,
                (double)allocatedBytes / iterations,
                checksum);
    }
}
