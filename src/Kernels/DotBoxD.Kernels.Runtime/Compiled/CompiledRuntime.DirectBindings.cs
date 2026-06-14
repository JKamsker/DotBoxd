namespace DotBoxD.Kernels.Runtime;

using System.Runtime.CompilerServices;
using DotBoxD.Kernels;

public static partial class CompiledRuntime
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanBulkChargeFuel(SandboxContext context, long fuelPerUnit, int count)
        => context.CanBulkChargeFuel(fuelPerUnit, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeBulkFuel(SandboxContext context, long fuelPerUnit, int count)
        => context.ChargeBulkFuel(fuelPerUnit, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanBulkChargeLoopIterations(SandboxContext context, int count, int fuelPerIteration)
        => context.CanBulkChargeLoopIterations(count, fuelPerIteration);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void RequireAdditionalCallDepth(SandboxContext context, int additionalDepth)
        => context.RequireAdditionalCallDepth(additionalDepth);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddRepeatedI32LoopRaw(
        SandboxContext context,
        int current,
        int iterations,
        int delta,
        int loopFuelPerIteration)
    {
        context.ChargeLoopIterations(iterations, loopFuelPerIteration);
        return SandboxInt32Math.AddRepeated(current, delta, iterations);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AddRemainderCycleI32LoopRaw(
        SandboxContext context,
        int current,
        int iterations,
        int divisor,
        int loopFuelPerIteration)
    {
        context.ChargeLoopIterations(iterations, loopFuelPerIteration);
        return SandboxInt32Math.AddRemainderCycleFromZero(current, iterations, divisor);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeFuel64(SandboxContext context, long amount) => context.ChargeFuel(amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeSandboxValue(SandboxContext context, SandboxValue value) => context.ChargeValue(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanBulkChargeSandboxValue(SandboxContext context, SandboxValue value, int count)
        => context.CanBulkChargeValue(value, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeSandboxValues(SandboxContext context, SandboxValue value, int count)
        => context.ChargeBulkValue(value, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanBulkChargeBindingCalls(SandboxContext context, string id, int count)
        => context.CanBulkChargeBindingCalls(context.Bindings.GetDescriptor(id), count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeBindingCalls(SandboxContext context, string id, int count)
        => context.ChargeBindingCalls(context.Bindings.GetDescriptor(id), count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanBulkChargeBindingCallsScaled(
        SandboxContext context,
        string id,
        int iterations,
        int callsPerIteration)
        => callsPerIteration > 0 &&
           CanScale(iterations, callsPerIteration, out var calls) &&
           context.CanBulkChargeBindingCalls(context.Bindings.GetDescriptor(id), calls);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeBindingCallsScaled(
        SandboxContext context,
        string id,
        int iterations,
        int callsPerIteration)
    {
        if (!CanScale(iterations, callsPerIteration, out var calls))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                $"binding call budget exhausted at {id}"));
        }

        context.ChargeBindingCalls(context.Bindings.GetDescriptor(id), calls);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StringLengthRaw(SandboxValue value) => ((StringValue)value).Value.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ListCountRaw(SandboxValue value)
        => value is ListValue list ? list.Values.Count : throw InvalidInput("expected list value");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ListReadFuelRaw(int count) => SandboxCollectionFuel.Read(count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ListGetI32Raw(SandboxValue value, int index)
    {
        var list = value as ListValue ?? throw InvalidInput("expected list value");
        var values = list.Values;
        if (index < 0 || index >= values.Count)
        {
            throw InvalidInput("list index is out of range");
        }

        return values[index] is I32Value item ? item.Value : throw InvalidInput("expected I32 value");
    }

    public static object ListI32ReaderRaw(SandboxValue value)
    {
        var list = value as ListValue ?? throw InvalidInput("expected list value");
        var values = list.Values;
        var items = new int[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not I32Value item)
            {
                throw InvalidInput("expected I32 value");
            }

            items[i] = item.Value;
        }

        return items;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ListI32ReaderGetRaw(object reader, int index)
    {
        var items = (int[])reader;
        if ((uint)index >= (uint)items.Length)
        {
            throw InvalidInput("list index is out of range");
        }

        return items[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ListI32ReaderGetRemainderRaw(object reader, int value, int divisor)
        => ListI32ReaderGetRaw(reader, SandboxInt32Math.Remainder(value, divisor));

    public static int ListI32ReaderAddRemainderCycleFromZeroRaw(
        SandboxContext context,
        object reader,
        int current,
        int iterations,
        int divisor,
        int loopFuelPerIteration,
        long readFuel)
    {
        if (iterations <= 0)
        {
            return current;
        }

        if (divisor <= 0)
        {
            throw InvalidInput("integer division by zero");
        }

        context.ChargeLoopIterations(iterations, loopFuelPerIteration);
        context.ChargeBulkFuel(readFuel, iterations);

        var items = (int[])reader;
        var cycles = iterations / divisor;
        var remainder = iterations % divisor;
        var total = current;
        if (cycles > 0)
        {
            total = SandboxInt32Math.Add(total, MultiplyCycleSum(SumItems(items, divisor), cycles));
        }

        return remainder == 0
            ? total
            : SandboxInt32Math.Add(total, SumItems(items, remainder));
    }

    private static int SumItems(int[] items, int count)
    {
        var total = 0;
        for (var i = 0; i < count; i++)
        {
            total = SandboxInt32Math.Add(total, ListI32ReaderGetRaw(items, i));
        }

        return total;
    }

    private static int MultiplyCycleSum(int cycleSum, int cycles)
    {
        var product = (long)cycleSum * cycles;
        if (product < int.MinValue || product > int.MaxValue)
        {
            throw InvalidInput("integer overflow");
        }

        return (int)product;
    }

    private static bool CanScale(int iterations, int callsPerIteration, out long calls)
    {
        calls = 0;
        if (iterations < 0 || callsPerIteration < 0)
        {
            return false;
        }

        calls = (long)iterations * callsPerIteration;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MapCountRaw(SandboxValue value)
        => value is MapValue map ? map.Values.Count : throw InvalidInput("expected map value");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MapGetI32Raw(SandboxValue value, SandboxValue key)
    {
        var map = value as MapValue ?? throw InvalidInput("expected map value");
        SandboxValueValidator.RequireType(key, map.KeyType, "map key type mismatch");
        if (!map.Values.TryGetValue(key, out var item))
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.NotFound,
                "map key was not found"));
        }

        return item is I32Value i32 ? i32.Value : throw InvalidInput("expected I32 value");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SqrtF64Raw(double value)
    {
        var result = Math.Sqrt(value);
        return double.IsFinite(result) ? result : throw InvalidInput("f64 value must be finite");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FloorF64Raw(double value) => Math.Floor(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double CeilF64Raw(double value) => Math.Ceiling(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double RoundF64Raw(double value) => Math.Round(value, MidpointRounding.ToEven);
}
