using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Benchmarks.Validation;

internal static class SandboxTypeValidationProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var type = CreateType();
        var declaredOpaqueIds = new HashSet<string>(StringComparer.Ordinal) { "PlayerId" };
        _ = MeasureLegacy(type, declaredOpaqueIds, Warmup);
        _ = MeasureDirect(type, declaredOpaqueIds, Warmup);

        var legacy = MeasureLegacy(type, declaredOpaqueIds, Iterations);
        var direct = MeasureDirect(type, declaredOpaqueIds, Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Print("legacy known+forbidden", legacy);
        Print("direct known only", direct);
    }

    private static SandboxType CreateType()
    {
        var playerId = SandboxType.Scalar("PlayerId");
        var inventoryEntry = SandboxType.Record([
            SandboxType.I32,
            SandboxType.String,
            SandboxType.Map(SandboxType.String, SandboxType.List(SandboxType.I64))
        ]);

        return SandboxType.Record([
            SandboxType.Map(playerId, SandboxType.List(inventoryEntry)),
            SandboxType.List(SandboxType.Map(SandboxType.String, SandboxType.Record([
                SandboxType.I64,
                SandboxType.F64,
                playerId
            ]))),
            playerId
        ]);
    }

    private static Measurement MeasureLegacy(
        SandboxType type,
        IReadOnlySet<string> declaredOpaqueIds,
        int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (type.IsKnown(declaredOpaqueIds) && !type.IsForbidden())
            {
                checksum++;
            }
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static Measurement MeasureDirect(
        SandboxType type,
        IReadOnlySet<string> declaredOpaqueIds,
        int iterations)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (type.IsKnown(declaredOpaqueIds))
            {
                checksum++;
            }
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-24} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.Checksum,10:N0} checksum");

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
