using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Runtime;

internal static class CompiledBindingStructuralValidationProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    private static readonly SandboxType ListType = SandboxType.List(SandboxType.I32);
    private static readonly SandboxType MapType = SandboxType.Map(SandboxType.String, SandboxType.I32);
    private static readonly SandboxType RecordType = SandboxType.Record([MapType, ListType]);

    private static readonly SandboxValue ListValue = SandboxValue.FromList(
        [SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)],
        SandboxType.I32);

    private static readonly SandboxValue RecordValue = SandboxValue.FromRecord(
        [
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1)
                },
                SandboxType.String,
                SandboxType.I32),
            ListValue
        ]);

    public static void Run()
    {
        _ = MeasureLegacy(Warmup);
        _ = MeasureDirect(Warmup);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var legacy = MeasureLegacy(Iterations);
        var direct = MeasureDirect(Iterations);

        Console.WriteLine("case                         validations   elapsed       allocated      matches");
        Write("legacy Type.Equals", legacy);
        Write("direct shape match", direct);
        Console.WriteLine(
            $"saved per validation: {(legacy.AllocatedBytes - direct.AllocatedBytes) / (double)legacy.Validations:N1} B");
    }

    private static RunSummary MeasureLegacy(int iterations)
    {
        var matched = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            matched += LegacyMatches(ListValue, ListType) ? 1 : 0;
            matched += LegacyMatches(RecordValue, RecordType) ? 1 : 0;
        }

        sw.Stop();
        return new RunSummary(
            iterations * 2,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            matched);
    }

    private static RunSummary MeasureDirect(int iterations)
    {
        var matched = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            matched += DirectMatches(ListValue, ListType) ? 1 : 0;
            matched += DirectMatches(RecordValue, RecordType) ? 1 : 0;
        }

        sw.Stop();
        return new RunSummary(
            iterations * 2,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            matched);
    }

    private static bool LegacyMatches(SandboxValue value, SandboxType expected)
        => value.Type.Equals(expected);

    private static bool DirectMatches(SandboxValue value, SandboxType expected)
    {
        if (expected.Arguments.Count == 0)
        {
            return ScalarTypeMatches(value, expected.Name);
        }

        if (expected.Name == "List" && expected.Arguments.Count == 1)
        {
            return value is ListValue list && list.ItemType.Equals(expected.Arguments[0]);
        }

        if (expected.Name == "Map" && expected.Arguments.Count == 2)
        {
            return value is MapValue map &&
                   map.KeyType.Equals(expected.Arguments[0]) &&
                   map.ValueType.Equals(expected.Arguments[1]);
        }

        if (expected.IsRecord && value is RecordValue record)
        {
            return RecordTypeMatches(record, expected);
        }

        return false;
    }

    private static bool ScalarTypeMatches(SandboxValue value, string expectedName)
        => value switch
        {
            UnitValue => expectedName == SandboxType.Unit.Name,
            BoolValue => expectedName == SandboxType.Bool.Name,
            I32Value => expectedName == SandboxType.I32.Name,
            I64Value => expectedName == SandboxType.I64.Name,
            F64Value => expectedName == SandboxType.F64.Name,
            StringValue => expectedName == SandboxType.String.Name,
            OpaqueIdValue opaque => string.Equals(opaque.TypeName, expectedName, StringComparison.Ordinal),
            SandboxPathValue => expectedName == SandboxType.SandboxPath.Name,
            SandboxUriValue => expectedName == SandboxType.SandboxUri.Name,
            _ => false
        };

    private static bool RecordTypeMatches(RecordValue record, SandboxType expected)
    {
        if (record.Fields.Count != expected.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < record.Fields.Count; i++)
        {
            if (!DirectMatches(record.Fields[i], expected.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static void Write(string name, RunSummary summary)
        => Console.WriteLine(
            $"{name,-25} {summary.Validations,11:N0} {summary.Milliseconds,8:N1} ms " +
            $"{summary.AllocatedBytes,13:N0} B {summary.Matches,11:N0}");

    private readonly record struct RunSummary(
        int Validations,
        double Milliseconds,
        long AllocatedBytes,
        int Matches);
}
