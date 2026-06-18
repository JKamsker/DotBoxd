using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Core;

internal static class MapRemoveProbe
{
    private const int Warmup = 1_000;
    private const int Iterations = 20_000;
    private const int Entries = 128;

    public static void Run()
    {
        var source = CreateSourceMap();
        var key = SandboxValue.FromInt32(Entries / 2);

        _ = MeasureLegacy(source, key, Warmup);
        _ = MeasureCurrent(source, key, Warmup);

        var legacy = MeasureLegacy(source, key, Iterations);
        var current = MeasureCurrent(source, key, Iterations);

        Console.WriteLine($"entries = {Entries:N0}");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Print("legacy copy remove", legacy);
        Print("structural remove", current);
    }

    private static MapValue CreateSourceMap()
    {
        var run = RunState.Create();
        SandboxValue map = CompiledRuntime.MapEmpty(run.Context, SandboxType.I32, SandboxType.I32);
        for (var i = 0; i < Entries; i++)
        {
            map = CompiledRuntime.MapSet(
                run.Context,
                map,
                SandboxValue.FromInt32(i),
                SandboxValue.FromInt32(i * 10));
        }

        return (MapValue)map;
    }

    private static Measurement MeasureLegacy(MapValue map, SandboxValue key, int iterations)
        => Measure(map, key, iterations, LegacyRemove);

    private static Measurement MeasureCurrent(MapValue map, SandboxValue key, int iterations)
        => Measure(
            map,
            key,
            iterations,
            static (context, source, removeKey) => CompiledRuntime.MapRemove(context, source, removeKey));

    private static Measurement Measure(
        MapValue map,
        SandboxValue key,
        int iterations,
        Func<SandboxContext, MapValue, SandboxValue, SandboxValue> remove)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var run = RunState.Create();
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var removed = (MapValue)remove(run.Context, map, key);
            checksum += removed.Values.Count;
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static SandboxValue LegacyRemove(SandboxContext context, MapValue map, SandboxValue key)
    {
        SandboxValueValidator.RequireType(map, map.Type, "map entry type mismatch");
        SandboxValueValidator.RequireType(key, map.KeyType, "map key type mismatch");
        context.ChargeFuel(SandboxCollectionFuel.Copy(map.Values.Count));
        var count = map.Values.ContainsKey(key) ? map.Values.Count - 1 : map.Values.Count;
        context.ChargeAllocation(AllocationBytes(count, 32, minimumOne: true));
        var values = new Dictionary<SandboxValue, SandboxValue>(map.Values);
        values.Remove(key);
        var removed = SandboxValue.FromMap(values, map.KeyType, map.ValueType);
        context.ChargeValue(removed);
        return removed;
    }

    private static long AllocationBytes(int elementCount, int bytesPerElement, bool minimumOne)
    {
        var chargedElements = minimumOne ? Math.Max(1L, elementCount) : Math.Max(0L, elementCount);
        return checked(chargedElements * bytesPerElement);
    }

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-20} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.Checksum,10:N0} checksum");

    private sealed class RunState
    {
        private RunState(SandboxContext context)
            => Context = context;

        public SandboxContext Context { get; }

        public static RunState Create()
        {
            var limits = new ResourceLimits(
                MaxFuel: long.MaxValue,
                MaxWallTime: TimeSpan.FromMinutes(5),
                MaxAllocatedBytes: long.MaxValue,
                MaxMapEntries: int.MaxValue,
                MaxTotalCollectionElements: long.MaxValue);
            var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
            return new RunState(new SandboxContext(
                SandboxRunId.New(),
                policy,
                new ResourceMeter(limits),
                new BindingRegistryBuilder().Build(),
                new InMemoryAuditSink(),
                CancellationToken.None));
        }
    }

    private readonly record struct Measurement(
        double Milliseconds,
        double NanosecondsPerOperation,
        long AllocatedBytes,
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
                checksum);
    }
}
