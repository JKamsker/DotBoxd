using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static SandboxValue? TryScalarToSandbox(object? value, Type type)
        => type switch
        {
            var t when t == typeof(bool) => SandboxValue.FromBool((bool)value!),
            var t when t == typeof(int) => SandboxValue.FromInt32((int)value!),
            var t when t == typeof(long) => SandboxValue.FromInt64((long)value!),
            var t when t == typeof(double) => SandboxValue.FromDouble((double)value!),
            var t when t == typeof(float) => SandboxValue.FromDouble((float)value!),
            var t when t == typeof(string) => SandboxValue.FromString((string)value!),
            var t when t == typeof(Guid) => SandboxValue.FromGuid((Guid)value!),
            var t when t == typeof(DateOnly) => SandboxValue.FromInt32(((DateOnly)value!).DayNumber),
            var t when t == typeof(TimeOnly) => SandboxValue.FromInt64(((TimeOnly)value!).Ticks),
            var t when t == typeof(TimeSpan) => SandboxValue.FromInt64(((TimeSpan)value!).Ticks),
            var t when t == typeof(CancellationToken) => SandboxValue.FromBool(((CancellationToken)value!).IsCancellationRequested),
            _ => null
        };

    private static bool TryScalarFromSandbox(SandboxValue value, Type type, out object? result)
    {
        result = (type, value) switch
        {
            (var t, BoolValue b) when t == typeof(bool) => b.Value,
            (var t, I32Value i) when t == typeof(int) => i.Value,
            (var t, I64Value l) when t == typeof(long) => l.Value,
            (var t, F64Value d) when t == typeof(double) => d.Value,
            (var t, F64Value d) when t == typeof(float) => DoubleToSingle(d.Value),
            (var t, StringValue s) when t == typeof(string) => s.Value,
            (var t, GuidValue g) when t == typeof(Guid) => g.Value,
            (var t, I32Value i) when t == typeof(DateOnly) => DateOnlyFromDayNumber(i.Value),
            (var t, I64Value l) when t == typeof(TimeOnly) => TimeOnlyFromTicks(l.Value),
            (var t, I64Value l) when t == typeof(TimeSpan) => new TimeSpan(l.Value),
            (var t, BoolValue b) when t == typeof(CancellationToken) => new CancellationToken(b.Value),
            _ => null
        };
        return result is not null;
    }

    private static Array ToArray(IReadOnlyList<SandboxValue> values, Type elementType)
    {
        var array = Array.CreateInstance(elementType, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            array.SetValue(FromSandboxValue(values[i], elementType), i);
        }

        return array;
    }

    // An enum marshals through its underlying integer; widths that overflow I32 (uint/long/ulong) use I64.
    private static bool EnumUsesI64(Type type)
    {
        var underlying = Enum.GetUnderlyingType(type);
        return underlying == typeof(uint) || underlying == typeof(long) || underlying == typeof(ulong);
    }

    // A declared ulong-backed enum value above long.MaxValue is carried as a negative I64 wire value. Reject
    // undeclared high-bit values here so outbound encode cannot produce a value inbound decode rejects.
    private static long EnumToInt64(object value, Type type)
        => Enum.GetUnderlyingType(type) == typeof(ulong)
            ? UInt64EnumToInt64(value, type)
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    private static long UInt64EnumToInt64(object value, Type type)
    {
        var bits = Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        if ((bits & (1UL << 63)) != 0UL && !NegativeBitsMatchUInt64Enum(type, bits))
        {
            throw EnumOutOfRange(type, unchecked((long)bits));
        }

        return unchecked((long)bits);
    }
}
