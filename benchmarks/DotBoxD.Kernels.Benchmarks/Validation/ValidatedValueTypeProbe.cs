using System.Diagnostics;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Validation;

internal static class ValidatedValueTypeProbe
{
    private const int Warmup = 10_000;
    private const int Iterations = 200_000;

    public static void Run()
    {
        var expectedType = CreateType();
        var value = CreateValue();

        _ = MeasureLegacy(value, expectedType, Warmup);
        _ = MeasureCurrent(value, expectedType, Warmup);

        var legacy = MeasureLegacy(value, expectedType, Iterations);
        var current = MeasureCurrent(value, expectedType, Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Print("legacy Type validation", legacy);
        Print("frame type validation", current);
    }

    private static SandboxType CreateType()
        => SandboxType.Record([
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            SandboxType.List(SandboxType.Record([
                SandboxType.I32,
                SandboxType.String,
                SandboxType.Map(SandboxType.String, SandboxType.I64)
            ]))
        ]);

    private static SandboxValue CreateValue()
        => SandboxValue.FromRecord([
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1),
                    [SandboxValue.FromString("two")] = SandboxValue.FromInt32(2)
                },
                SandboxType.String,
                SandboxType.I32),
            SandboxValue.FromList(
                [
                    SandboxValue.FromRecord([
                        SandboxValue.FromInt32(7),
                        SandboxValue.FromString("alpha"),
                        SandboxValue.FromMap(
                            new Dictionary<SandboxValue, SandboxValue>
                            {
                                [SandboxValue.FromString("score")] = SandboxValue.FromInt64(42)
                            },
                            SandboxType.String,
                            SandboxType.I64)
                    ])
                ],
                SandboxType.Record([
                    SandboxType.I32,
                    SandboxType.String,
                    SandboxType.Map(SandboxType.String, SandboxType.I64)
                ]))
        ]);

    private static Measurement MeasureLegacy(SandboxValue value, SandboxType expectedType, int iterations)
        => Measure(value, expectedType, iterations, LegacyRequireType);

    private static Measurement MeasureCurrent(SandboxValue value, SandboxType expectedType, int iterations)
        => Measure(
            value,
            expectedType,
            iterations,
            static (current, expected) => SandboxValueValidator.RequireType(current, expected, "bad input"));

    private static Measurement Measure(
        SandboxValue value,
        SandboxType expectedType,
        int iterations,
        Action<SandboxValue, SandboxType> validate)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            validate(value, expectedType);
            checksum++;
        }

        sw.Stop();
        return Measurement.Create(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
            checksum,
            iterations);
    }

    private static void LegacyRequireType(SandboxValue value, SandboxType expectedType)
    {
        if (!expectedType.IsKnown())
        {
            throw Error();
        }

        var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Frame>();
        stack.Push(new Frame(value, expectedType, Exit: false));
        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            if (frame.Exit)
            {
                active.Remove(frame.Value);
                continue;
            }

            if (frame.Value.Type != frame.ExpectedType)
            {
                throw Error();
            }

            switch (frame.Value)
            {
                case ListValue list:
                    PushList(list, active, stack);
                    break;
                case MapValue map:
                    PushMap(map, active, stack);
                    break;
                case RecordValue record:
                    PushRecord(record, frame.ExpectedType, active, stack);
                    break;
            }
        }
    }

    private static void PushList(
        ListValue list,
        HashSet<object> active,
        Stack<Frame> stack)
    {
        Enter(list, active);
        stack.Push(new Frame(list, SandboxType.List(list.ItemType), Exit: true));
        for (var i = list.Values.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(list.Values[i], list.ItemType, Exit: false));
        }
    }

    private static void PushMap(
        MapValue map,
        HashSet<object> active,
        Stack<Frame> stack)
    {
        Enter(map, active);
        stack.Push(new Frame(map, SandboxType.Map(map.KeyType, map.ValueType), Exit: true));
        foreach (var pair in map.Values)
        {
            stack.Push(new Frame(pair.Value, map.ValueType, Exit: false));
            stack.Push(new Frame(pair.Key, map.KeyType, Exit: false));
        }
    }

    private static void PushRecord(
        RecordValue record,
        SandboxType expectedType,
        HashSet<object> active,
        Stack<Frame> stack)
    {
        Enter(record, active);
        stack.Push(new Frame(record, expectedType, Exit: true));
        for (var i = record.Fields.Count - 1; i >= 0; i--)
        {
            stack.Push(new Frame(record.Fields[i], expectedType.Arguments[i], Exit: false));
        }
    }

    private static void Enter(object value, HashSet<object> active)
    {
        if (!active.Add(value))
        {
            throw Error();
        }
    }

    private static SandboxRuntimeException Error()
        => new(new SandboxError(SandboxErrorCode.InvalidInput, "bad input"));

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-24} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.Checksum,10:N0} checksum");

    private readonly record struct Frame(SandboxValue Value, SandboxType ExpectedType, bool Exit);

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
