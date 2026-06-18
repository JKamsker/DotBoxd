using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Core;

internal static class ListAddTypeMatchProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    private static readonly SandboxType ItemType = SandboxType.Record([
        SandboxType.I32,
        SandboxType.String,
        SandboxType.Map(SandboxType.String, SandboxType.I64)
    ]);

    private static readonly SandboxValue Item = SandboxValue.FromRecord([
        SandboxValue.FromInt32(7),
        SandboxValue.FromString("alpha"),
        SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("score")] = SandboxValue.FromInt64(42)
            },
            SandboxType.String,
            SandboxType.I64)
    ]);

    public static void Run()
    {
        _ = MeasureLegacy(Warmup);
        _ = MeasureCurrent(Warmup);

        var legacy = MeasureLegacy(Iterations);
        var current = MeasureCurrent(Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Print("legacy item.Type", legacy);
        Print("exact frame match", current);
    }

    private static Measurement MeasureLegacy(int iterations)
        => Measure(iterations, static () => Item.Type.Equals(ItemType));

    private static Measurement MeasureCurrent(int iterations)
        => Measure(iterations, static () => MatchesExactType(Item, ItemType));

    private static Measurement Measure(int iterations, Func<bool> matches)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (matches())
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

    private static bool MatchesExactType(SandboxValue value, SandboxType expectedType)
    {
        if (expectedType.Name == SandboxType.RecordName)
        {
            return value is RecordValue record && RecordMatches(record, expectedType);
        }

        if (expectedType.Arguments.Count == 0)
        {
            return ScalarMatches(value, expectedType.Name);
        }

        if (expectedType.Name == "List" && expectedType.Arguments.Count == 1)
        {
            return value is ListValue list && list.ItemType.Equals(expectedType.Arguments[0]);
        }

        return expectedType.Name == "Map" &&
               expectedType.Arguments.Count == 2 &&
               value is MapValue map &&
               map.KeyType.Equals(expectedType.Arguments[0]) &&
               map.ValueType.Equals(expectedType.Arguments[1]);
    }

    private static bool RecordMatches(RecordValue record, SandboxType expectedType)
    {
        if (record.Fields.Count != expectedType.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < record.Fields.Count; i++)
        {
            if (!MatchesExactType(record.Fields[i], expectedType.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ScalarMatches(SandboxValue value, string expectedName)
        => value switch
        {
            UnitValue => expectedName == SandboxType.Unit.Name,
            BoolValue => expectedName == SandboxType.Bool.Name,
            I32Value => expectedName == SandboxType.I32.Name,
            I64Value => expectedName == SandboxType.I64.Name,
            F64Value => expectedName == SandboxType.F64.Name,
            StringValue => expectedName == SandboxType.String.Name,
            OpaqueIdValue id => string.Equals(id.TypeName, expectedName, StringComparison.Ordinal),
            SandboxPathValue => expectedName == SandboxType.SandboxPath.Name,
            SandboxUriValue => expectedName == SandboxType.SandboxUri.Name,
            _ => false
        };

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-18} {measurement.Milliseconds,8:N1} ms " +
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
