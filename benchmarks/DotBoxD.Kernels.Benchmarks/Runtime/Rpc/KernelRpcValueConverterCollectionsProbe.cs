using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class KernelRpcValueConverterCollectionsProbe
{
    private const int Warmup = 2_000;
    private const int EmptyIterations = 1_000_000;
    private const int MapIterations = 200_000;

    public static void Run()
    {
        Console.WriteLine("Kernel RPC value converter collection probe");
        RunEmptyLane();
        RunMapLane(8);
        RunMapLane(32);
    }

    private static void RunEmptyLane()
    {
        var listType = SandboxType.List(SandboxType.I32);
        var emptyWireList = KernelRpcValue.List(Array.Empty<KernelRpcValue>());
        var emptySandboxList = SandboxValue.FromOwnedList(Array.Empty<SandboxValue>(), SandboxType.I32);
        var emptySandboxRecord = SandboxValue.FromOwnedRecord(Array.Empty<SandboxValue>());
        var emptySandboxMap = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>(),
            SandboxType.String,
            SandboxType.I32);

        _ = Measure(Warmup, static state => LegacyWireListToSandbox(state), new WireListState(emptyWireList, listType));
        _ = Measure(Warmup, static state => CurrentWireListToSandbox(state), new WireListState(emptyWireList, listType));
        _ = Measure(Warmup, static value => LegacySandboxListToWire(value), (ListValue)emptySandboxList);
        _ = Measure(Warmup, static value => CurrentSandboxValueToWire(value), emptySandboxList);
        _ = Measure(Warmup, static value => LegacySandboxRecordToWire(value), (RecordValue)emptySandboxRecord);
        _ = Measure(Warmup, static value => CurrentSandboxValueToWire(value), emptySandboxRecord);
        _ = Measure(Warmup, static value => LegacySandboxMapToWire(value), (MapValue)emptySandboxMap);
        _ = Measure(Warmup, static value => CurrentSandboxValueToWire(value), emptySandboxMap);

        Console.WriteLine($"empty iterations = {EmptyIterations:N0}");
        Print("legacy empty wire list -> sandbox", Measure(
            EmptyIterations,
            static state => LegacyWireListToSandbox(state),
            new WireListState(emptyWireList, listType)));
        Print("current empty wire list -> sandbox", Measure(
            EmptyIterations,
            static state => CurrentWireListToSandbox(state),
            new WireListState(emptyWireList, listType)));
        Print("legacy empty sandbox list -> wire", Measure(
            EmptyIterations,
            static value => LegacySandboxListToWire(value),
            (ListValue)emptySandboxList));
        Print("current empty sandbox list -> wire", Measure(
            EmptyIterations,
            static value => CurrentSandboxValueToWire(value),
            emptySandboxList));
        Print("legacy empty sandbox record -> wire", Measure(
            EmptyIterations,
            static value => LegacySandboxRecordToWire(value),
            (RecordValue)emptySandboxRecord));
        Print("current empty sandbox record -> wire", Measure(
            EmptyIterations,
            static value => CurrentSandboxValueToWire(value),
            emptySandboxRecord));
        Print("legacy empty sandbox map -> wire", Measure(
            EmptyIterations,
            static value => LegacySandboxMapToWire(value),
            (MapValue)emptySandboxMap));
        Print("current empty sandbox map -> wire", Measure(
            EmptyIterations,
            static value => CurrentSandboxValueToWire(value),
            emptySandboxMap));
    }

    private static void RunMapLane(int entries)
    {
        var map = CreateWireMap(entries);
        var expectedType = SandboxType.Map(SandboxType.String, SandboxType.I32);
        var state = new WireMapState(map, expectedType, SandboxType.String, SandboxType.I32);

        _ = Measure(Warmup, static value => LegacyWireMapToSandbox(value), state);
        _ = Measure(Warmup, static value => CurrentWireMapToSandbox(value), state);

        Console.WriteLine($"map iterations = {MapIterations:N0}");
        Print($"legacy wire map -> sandbox ({entries,2})", Measure(
            MapIterations,
            static value => LegacyWireMapToSandbox(value),
            state));
        Print($"current wire map -> sandbox ({entries,2})", Measure(
            MapIterations,
            static value => CurrentWireMapToSandbox(value),
            state));
    }

    private static int LegacyWireListToSandbox(WireListState state)
    {
        state.Value.RequireKind(KernelRpcValueKind.List);
        var items = new SandboxValue[state.Value.ItemCount];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = KernelRpcValueConverter.ToSandboxValue(state.Value.GetItem(i), state.ListType.Arguments[0]);
        }

        return ((ListValue)SandboxValue.FromOwnedList(items, state.ListType.Arguments[0])).Values.Count;
    }

    private static int CurrentWireListToSandbox(WireListState state)
        => ((ListValue)KernelRpcValueConverter.ToSandboxValue(state.Value, state.ListType)).Values.Count;

    private static int LegacySandboxListToWire(ListValue value)
    {
        var converted = new KernelRpcValue[value.Values.Count];
        for (var i = 0; i < value.Values.Count; i++)
        {
            converted[i] = KernelRpcValueConverter.FromSandboxValue(value.Values[i]);
        }

        return KernelRpcValue.List(converted).ItemCount;
    }

    private static int LegacySandboxRecordToWire(RecordValue value)
    {
        var converted = new KernelRpcValue[value.Fields.Count];
        for (var i = 0; i < value.Fields.Count; i++)
        {
            converted[i] = KernelRpcValueConverter.FromSandboxValue(value.Fields[i]);
        }

        return KernelRpcValue.Record(converted).ItemCount;
    }

    private static int LegacySandboxMapToWire(MapValue value)
    {
        var entries = new KernelRpcValue[value.Values.Count * 2];
        var index = 0;
        foreach (var pair in value.Entries)
        {
            entries[index++] = KernelRpcValueConverter.FromSandboxValue(pair.Key);
            entries[index++] = KernelRpcValueConverter.FromSandboxValue(pair.Value);
        }

        return KernelRpcValue.Map(entries).ItemCount;
    }

    private static int CurrentSandboxValueToWire(SandboxValue value)
        => KernelRpcValueConverter.FromSandboxValue(value).ItemCount;

    private static int LegacyWireMapToSandbox(WireMapState state)
    {
        state.Value.RequireKind(KernelRpcValueKind.Map);
        var entries = new Dictionary<SandboxValue, SandboxValue>(state.Value.ItemCount / 2);
        for (var i = 0; i + 1 < state.Value.ItemCount; i += 2)
        {
            var key = KernelRpcValueConverter.ToSandboxValue(state.Value.GetItem(i), state.KeyType);
            var value = KernelRpcValueConverter.ToSandboxValue(state.Value.GetItem(i + 1), state.ValueType);
            if (!entries.TryAdd(key, value))
            {
                throw new FormatException("Server extension IPC map payload contains a duplicate key.");
            }
        }

        return ((MapValue)SandboxValue.FromMap(entries, state.KeyType, state.ValueType)).Values.Count;
    }

    private static int CurrentWireMapToSandbox(WireMapState state)
        => ((MapValue)KernelRpcValueConverter.ToSandboxValue(state.Value, state.ExpectedType)).Values.Count;

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
            $"{name,-40} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op " +
            $"{measurement.Checksum,10:N0} checksum");

    private sealed record WireListState(KernelRpcValue Value, SandboxType ListType);

    private sealed record WireMapState(
        KernelRpcValue Value,
        SandboxType ExpectedType,
        SandboxType KeyType,
        SandboxType ValueType);

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
