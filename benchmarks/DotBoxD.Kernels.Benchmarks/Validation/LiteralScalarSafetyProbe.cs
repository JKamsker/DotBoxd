using System.Diagnostics;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Benchmarks.Validation;

internal static class LiteralScalarSafetyProbe
{
    private const int Warmup = 20_000;
    private const int Iterations = 1_000_000;

    public static void Run()
    {
        var value = SandboxValue.FromInt32(42);
        _ = MeasureLegacy(value, Warmup);
        _ = MeasureDirect(value, Warmup);

        var legacy = MeasureLegacy(value, Iterations);
        var direct = MeasureDirect(value, Iterations);

        Console.WriteLine($"iterations = {Iterations:N0}");
        Print("legacy scalar safety walks", legacy);
        Print("direct scalar safety checks", direct);
    }

    private static void Print(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.AllocatedBytes,14:N0} B {measurement.Checksum,8:N0} checksum");

    private static Measurement MeasureLegacy(SandboxValue value, int iterations)
        => Measure(value, iterations, static item => LegacyContainsDangerousReference(item), static item => LegacyValidate(item));

    private static Measurement MeasureDirect(SandboxValue value, int iterations)
        => Measure(value, iterations, static item => ContainsDangerousScalarReference(item), static item => ValidateScalar(item));

    private static Measurement Measure(
        SandboxValue value,
        int iterations,
        Func<SandboxValue, bool> containsDangerousReference,
        Func<SandboxValue, bool> validate)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var checksum = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            if (containsDangerousReference(value))
            {
                checksum++;
            }

            if (validate(value))
            {
                checksum += 2;
            }
        }

        sw.Stop();
        return new Measurement(
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before,
            checksum);
    }

    private static bool LegacyValidate(SandboxValue value)
    {
        var allocates = false;
        foreach (var current in Flatten(value))
        {
            allocates |= ValidateScalar(current);
        }

        return allocates;
    }

    private static bool LegacyContainsDangerousReference(SandboxValue value)
    {
        foreach (var current in Flatten(value))
        {
            if (ContainsDangerousScalarReference(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsDangerousScalarReference(SandboxValue value)
    {
        var text = value switch
        {
            StringValue item => item.Value,
            OpaqueIdValue item => item.Value,
            SandboxPathValue item => item.Value.RelativePath,
            SandboxUriValue item => item.Value.Value,
            _ => null
        };

        return text is not null && SandboxDescriptorGuards.ContainsForbiddenDescriptor(text);
    }

    private static bool ValidateScalar(SandboxValue value)
        => value switch
        {
            StringValue => true,
            OpaqueIdValue => true,
            SandboxPathValue => true,
            SandboxUriValue => true,
            ListValue or MapValue or RecordValue => true,
            _ => false
        };

    private static IEnumerable<SandboxValue> Flatten(SandboxValue value)
    {
        var stack = new Stack<SandboxValue>();
        stack.Push(value);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            switch (current)
            {
                case ListValue list:
                    for (var i = list.Values.Count - 1; i >= 0; i--)
                    {
                        stack.Push(list.Values[i]);
                    }

                    break;
                case MapValue map:
                    foreach (var pair in map.Values)
                    {
                        stack.Push(pair.Value);
                        stack.Push(pair.Key);
                    }

                    break;
                case RecordValue record:
                    for (var i = record.Fields.Count - 1; i >= 0; i--)
                    {
                        stack.Push(record.Fields[i]);
                    }

                    break;
            }
        }
    }

    private readonly record struct Measurement(double Milliseconds, long AllocatedBytes, int Checksum);
}
