namespace DotBoxD.Plugins.Runtime.Rpc;

using System.Globalization;

public static partial class KernelRpcMarshaller
{
    private static float DoubleToSingle(double value)
    {
        var result = (float)value;
        if (double.IsFinite(value) && !float.IsFinite(result))
        {
            throw new NotSupportedException(
                $"Server extension cannot marshal finite F64 value '{value}' to System.Single without overflow.");
        }

        return result;
    }

    private static object EnumFromInt32(Type type, int value)
    {
        var underlying = Enum.GetUnderlyingType(type);
        if (underlying == typeof(byte) && (value < byte.MinValue || value > byte.MaxValue) ||
            underlying == typeof(sbyte) && (value < sbyte.MinValue || value > sbyte.MaxValue) ||
            underlying == typeof(short) && (value < short.MinValue || value > short.MaxValue) ||
            underlying == typeof(ushort) && (value < ushort.MinValue || value > ushort.MaxValue))
        {
            throw EnumOutOfRange(type, value);
        }

        return Enum.ToObject(type, value);
    }

    private static object EnumFromInt64(Type type, long value)
    {
        var underlying = Enum.GetUnderlyingType(type);
        if (underlying == typeof(uint))
        {
            if (value < uint.MinValue || value > uint.MaxValue)
            {
                throw EnumOutOfRange(type, value);
            }

            return Enum.ToObject(type, unchecked((uint)value));
        }

        if (underlying == typeof(ulong))
        {
            return UInt64EnumFromInt64(type, value);
        }

        return Enum.ToObject(type, value);
    }

    private static NotSupportedException EnumOutOfRange(Type type, object value)
        => new($"Server extension enum value '{value}' is outside the underlying range for '{type}'.");

    private static object UInt64EnumFromInt64(Type type, long value)
    {
        var bits = unchecked((ulong)value);
        if (value < 0 && !NegativeBitsMatchUInt64Enum(type, bits))
        {
            throw EnumOutOfRange(type, value);
        }

        return Enum.ToObject(type, bits);
    }

    private static bool NegativeBitsMatchUInt64Enum(Type type, ulong bits)
        => type.IsDefined(typeof(FlagsAttribute), inherit: false)
            ? (bits & ~UInt64EnumDeclaredMask(type)) == 0UL
            : UInt64EnumDeclaredValues(type).Contains(bits);

    private static ulong UInt64EnumDeclaredMask(Type type)
    {
        ulong mask = 0;
        foreach (var value in UInt64EnumDeclaredValues(type))
        {
            mask |= value;
        }

        return mask;
    }

    private static IEnumerable<ulong> UInt64EnumDeclaredValues(Type type)
    {
        foreach (var value in Enum.GetValues(type))
        {
            yield return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
    }
}
