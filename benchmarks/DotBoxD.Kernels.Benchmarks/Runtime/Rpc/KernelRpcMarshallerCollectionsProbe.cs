using System.Collections;
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
        RunEmptyListEncodeLane();
        RunMapLane(0);
        RunMapLane(4);
        RunMapLane(32);
        RunFallbackDecodeLane(4);
        RunFallbackDecodeLane(32);
        RunMapEnumerationLane(32);
    }

    private static void RunEmptyListEncodeLane()
    {
        var source = Array.Empty<int>();
        var state = new EmptyCollectionState(source, SandboxType.I32);

        _ = Measure(Warmup, static value => LegacyEmptyCollectionToSandbox(value), state);
        _ = Measure(Warmup, static value => CurrentEmptyCollectionToSandbox(value), state);

        var legacy = Measure(Iterations, static value => LegacyEmptyCollectionToSandbox(value), state);
        var current = Measure(Iterations, static value => CurrentEmptyCollectionToSandbox(value), state);

        Print("legacy empty object list -> sandbox", legacy);
        Print("current empty object list -> sandbox", current);
    }

    private static void RunFallbackDecodeLane(int entries)
    {
        var list = CreateSandboxList(entries);
        var map = CreateSandboxMap(entries);
        var listType = typeof(List<int>);
        var mapType = typeof(Dictionary<string, int>);

        _ = Measure(Warmup, static state =>
            ((List<int>)KernelRpcMarshaller.FromSandboxValue(state.List, state.Type)!).Count,
            new MarshallerListState(list, listType));
        _ = Measure(Warmup, static state =>
            ((Dictionary<string, int>)KernelRpcMarshaller.FromSandboxValue(state.Map, state.Type)!).Count,
            new MarshallerMapState(map, mapType));

        var decodedList = Measure(Iterations, static state =>
            ((List<int>)KernelRpcMarshaller.FromSandboxValue(state.List, state.Type)!).Count,
            new MarshallerListState(list, listType));
        var decodedMap = Measure(Iterations, static state =>
            ((Dictionary<string, int>)KernelRpcMarshaller.FromSandboxValue(state.Map, state.Type)!).Count,
            new MarshallerMapState(map, mapType));

        Print($"sandbox list -> object list ({entries,2})", decodedList);
        Print($"sandbox map -> object dictionary ({entries,2})", decodedMap);
    }

    private static void RunMapEnumerationLane(int entries)
    {
        var map = CreateSandboxMap(entries);
        var type = typeof(Dictionary<string, int>);

        _ = Measure(Warmup, static state =>
            KernelRpcValueConverter.FromSandboxValue(state.Map).ItemCount, new SandboxMapState(map));
        _ = Measure(Warmup, static state =>
            ((Dictionary<string, int>)KernelRpcMarshaller.FromSandboxValue(state.Map, state.Type)!).Count,
            new MarshallerMapState(map, type));
        _ = Measure(Warmup, static state =>
            KernelRpcBinaryCodec.EncodeValue(state.Map).Length, new SandboxMapState(map));

        var wire = Measure(Iterations, static state =>
            KernelRpcValueConverter.FromSandboxValue(state.Map).ItemCount, new SandboxMapState(map));
        var runtime = Measure(Iterations, static state =>
            ((Dictionary<string, int>)KernelRpcMarshaller.FromSandboxValue(state.Map, state.Type)!).Count,
            new MarshallerMapState(map, type));
        var binary = Measure(Iterations, static state =>
            KernelRpcBinaryCodec.EncodeValue(state.Map).Length, new SandboxMapState(map));

        Print($"sandbox map -> wire ({entries,2})", wire);
        Print($"sandbox map -> object dictionary ({entries,2})", runtime);
        Print($"sandbox map -> binary ({entries,2})", binary);
    }

    private static int LegacyEmptyCollectionToSandbox(EmptyCollectionState state)
    {
        var items = new SandboxValue[state.Collection.Count];
        return ((ListValue)SandboxValue.FromOwnedList(items, state.ItemType)).Values.Count;
    }

    private static int CurrentEmptyCollectionToSandbox(EmptyCollectionState state)
    {
        var items = state.Collection.Count == 0
            ? Array.Empty<SandboxValue>()
            : new SandboxValue[state.Collection.Count];
        return ((ListValue)SandboxValue.FromOwnedList(items, state.ItemType)).Values.Count;
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

    private static MapValue CreateSandboxMap(int entries)
    {
        var source = new Dictionary<SandboxValue, SandboxValue>(entries);
        for (var i = 0; i < entries; i++)
        {
            source[SandboxValue.FromString($"key-{i}")] = SandboxValue.FromInt32(i);
        }

        return (MapValue)SandboxValue.FromMap(source, SandboxType.String, SandboxType.I32);
    }

    private static ListValue CreateSandboxList(int entries)
    {
        var source = new SandboxValue[entries];
        for (var i = 0; i < entries; i++)
        {
            source[i] = SandboxValue.FromInt32(i);
        }

        return (ListValue)SandboxValue.FromList(source, SandboxType.I32);
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

    private sealed record EmptyCollectionState(ICollection Collection, SandboxType ItemType);

    private sealed record SandboxMapState(MapValue Map);

    private sealed record MarshallerListState(ListValue List, Type Type);

    private sealed record MarshallerMapState(MapValue Map, Type Type);

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
