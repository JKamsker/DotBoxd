using System.Diagnostics;
using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Core;

internal static class LiteralCollectionConstructionProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 500_000;
    private static readonly Func<SandboxType, SandboxContext, SandboxValue> InterpreterMapEmpty =
        CreateInterpreterMapEmpty();

    public static void Run()
    {
        _ = MeasureListLiteral(Warmup, 8);
        _ = MeasureMapLiteral(Warmup, 8);
        _ = MeasureMapEmpty(Warmup);
        _ = MeasureInterpreterMapEmpty(Warmup);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Print("list literal arity 8", MeasureListLiteral(Iterations, 8));
        Print("list literal arity 32", MeasureListLiteral(Iterations, 32));
        Print("map literal arity 8", MeasureMapLiteral(Iterations, 8));
        Print("map literal arity 32", MeasureMapLiteral(Iterations, 32));
        Print("map.empty", MeasureMapEmpty(Iterations));
        Print("interpreter map.empty", MeasureInterpreterMapEmpty(Iterations));
    }

    private static Measurement MeasureListLiteral(int iterations, int arity)
        => Measure(iterations, arity, static (context, values) =>
            CompiledRuntime.ListLiteral(context, SandboxType.I32, values));

    private static Measurement MeasureMapLiteral(int iterations, int arity)
        => Measure(iterations, arity, static (context, values) =>
        {
            var keys = new SandboxValue[values.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = SandboxValue.FromInt32(i);
            }

            return CompiledRuntime.MapLiteral(context, SandboxType.I32, SandboxType.I32, keys, values);
        });

    private static Measurement MeasureMapEmpty(int iterations)
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
            var value = CompiledRuntime.MapEmpty(context, SandboxType.I32, SandboxType.I32);
            checksum += ((MapValue)value).Values.Count;
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static Measurement MeasureInterpreterMapEmpty(int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var context = CreateContext();
        var mapType = SandboxType.Map(SandboxType.I32, SandboxType.I32);
        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var value = InterpreterMapEmpty(mapType, context);
            checksum += ((MapValue)value).Values.Count;
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
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
            // Emitted literal code allocates fresh owned arrays immediately before the runtime call.
            // Keep them in the measured path because the optimization removes a second defensive copy.
            var values = CreateValues(arity, i);
            var value = build(context, values);
            checksum += value switch
            {
                ListValue list => list.Values.Count,
                MapValue map => map.Values.Count,
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
            MaxMapEntries: int.MaxValue,
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

    private static Func<SandboxType, SandboxContext, SandboxValue> CreateInterpreterMapEmpty()
    {
        var type = Type.GetType(
            "DotBoxD.Kernels.Interpreter.Internal.CollectionOperations, DotBoxD.Kernels.Interpreter",
            throwOnError: true)!;
        var method = type.GetMethod("CreateMap", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, "CreateMap");
        return (Func<SandboxType, SandboxContext, SandboxValue>)Delegate.CreateDelegate(
            typeof(Func<SandboxType, SandboxContext, SandboxValue>),
            method);
    }

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-22} {measurement.Milliseconds,8:N1} ms " +
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
