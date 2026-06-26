namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static float DoubleToSingle(double value)
    {
        var result = (float)value;
        if (!float.IsFinite(result))
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

        return underlying == typeof(ulong)
            ? Enum.ToObject(type, unchecked((ulong)value))
            : Enum.ToObject(type, value);
    }

    private static NotSupportedException EnumOutOfRange(Type type, object value)
        => new($"Server extension enum value '{value}' is outside the underlying range for '{type}'.");
}
