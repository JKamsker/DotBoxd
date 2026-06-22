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
            (var t, F64Value d) when t == typeof(float) => (float)d.Value,
            (var t, StringValue s) when t == typeof(string) => s.Value,
            (var t, GuidValue g) when t == typeof(Guid) => g.Value,
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

    // A ulong-backed enum value above long.MaxValue overflows a range-checked Convert.ToInt64; reinterpret its
    // bits instead so the value carries losslessly (decode uses Enum.ToObject, which is also bit-preserving).
    private static long EnumToInt64(object value, Type type)
        => Enum.GetUnderlyingType(type) == typeof(ulong)
            ? unchecked((long)Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture))
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
}
