using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Core;

internal static class MapSetReplaceProbe
{
    private const int Warmup = 1_000;
    private const int Iterations = 20_000;
    private const int Entries = 128;

    public static void Run()
    {
        var source = CreateSourceMap();
        var key = SandboxValue.FromInt32(Entries / 2);
        var values = new[] { SandboxValue.FromInt32(123), SandboxValue.FromInt32(456) };

        _ = Measure(source, key, values, Warmup);

        var measurement = Measure(source, key, values, Iterations);

        Console.WriteLine($"entries = {Entries:N0}");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Console.WriteLine(
            $"map.set replace {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.AllocatedBytes / (double)Iterations,8:N1} B/op " +
            $"{measurement.Checksum,10:N0} checksum");
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

    private static Measurement Measure(
        MapValue source,
        SandboxValue key,
        IReadOnlyList<SandboxValue> values,
        int iterations)
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
            var updated = (MapValue)CompiledRuntime.MapSet(run.Context, source, key, values[i & 1]);
            checksum += updated.Values.Count;
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

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
