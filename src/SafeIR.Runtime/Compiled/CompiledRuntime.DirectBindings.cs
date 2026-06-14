namespace SafeIR.Runtime;

using System.Runtime.CompilerServices;
using SafeIR;

public static partial class CompiledRuntime
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanBulkChargeFuel(SandboxContext context, long fuelPerUnit, int count)
        => context.CanBulkChargeFuel(fuelPerUnit, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeBulkFuel(SandboxContext context, long fuelPerUnit, int count)
        => context.ChargeBulkFuel(fuelPerUnit, count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeFuel64(SandboxContext context, long amount) => context.ChargeFuel(amount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanBulkChargeBindingCalls(SandboxContext context, string id, int count)
        => context.CanBulkChargeBindingCalls(context.Bindings.GetDescriptor(id), count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ChargeBindingCalls(SandboxContext context, string id, int count)
        => context.ChargeBindingCalls(context.Bindings.GetDescriptor(id), count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int StringLengthRaw(SandboxValue value) => ((StringValue)value).Value.Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ListCountRaw(SandboxValue value)
        => value is ListValue list ? list.Values.Count : throw InvalidInput("expected list value");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ListReadFuelRaw(int count) => SandboxCollectionFuel.Read(count);

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
