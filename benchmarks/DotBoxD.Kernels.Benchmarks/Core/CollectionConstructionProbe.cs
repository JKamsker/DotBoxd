using System.Diagnostics;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Core;

internal static class CollectionConstructionProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 500_000;

    public static void Run()
    {
        _ = Measure(Warmup, arity: 8, static (context, values) => CompiledRuntime.ListOf(context, values));
        _ = Measure(Warmup, arity: 8, static (context, values) => CompiledRuntime.RecordNew(context, values));

        Console.WriteLine($"iterations = {Iterations:N0}");
        Print("list.of arity 8", Measure(Iterations, 8, static (context, values) => CompiledRuntime.ListOf(context, values)));
        Print("list.of arity 32", Measure(Iterations, 32, static (context, values) => CompiledRuntime.ListOf(context, values)));
        Print("record.new arity 8", Measure(Iterations, 8, static (context, values) => CompiledRuntime.RecordNew(context, values)));
        Print("record.new arity 32", Measure(Iterations, 32, static (context, values) => CompiledRuntime.RecordNew(context, values)));
    }

    private static Measurement Measure(
        int iterations,
        int arity,
        Func<SandboxContext, SandboxValue[], SandboxValue> build)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext();
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var values = CreateValues(arity, i);
            var value = build(context, values);
            checksum += value switch
            {
                ListValue list => list.Values.Count,
                RecordValue record => record.Fields.Count,
                _ => 0
            };
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static SandboxValue[] CreateValues(int arity, int seed)
    {
        var values = new SandboxValue[arity];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = SandboxValue.FromInt32((seed + i) & 255);
        }

        return values;
    }

    private static SandboxContext CreateContext()
    {
        var limits = new ResourceLimits(
            MaxFuel: long.MaxValue,
            MaxWallTime: TimeSpan.FromMinutes(5),
            MaxAllocatedBytes: long.MaxValue,
            MaxListLength: int.MaxValue,
            MaxTotalCollectionElements: long.MaxValue);
        var policy = SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits };
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(limits),
            new BindingRegistryBuilder().Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-20} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op {measurement.Checksum,10:N0} checksum");

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
