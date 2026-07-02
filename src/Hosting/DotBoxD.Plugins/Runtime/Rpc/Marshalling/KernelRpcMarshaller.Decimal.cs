using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static bool IsDecimalWireType(Type type)
        => type == typeof(decimal);

    private static SandboxType DecimalWireSandboxType()
        => SandboxType.Record([SandboxType.I32, SandboxType.I32, SandboxType.I32, SandboxType.I32]);

    private static bool TryDecimalToSandboxValue(object value, Type type, out SandboxValue sandbox)
    {
        if (!IsDecimalWireType(type))
        {
            sandbox = null!;
            return false;
        }

        sandbox = DecimalToSandboxValue((decimal)value);
        return true;
    }

    private static bool TryDecimalFromSandboxValue(SandboxValue value, Type type, out object? result)
    {
        if (!IsDecimalWireType(type))
        {
            result = null;
            return false;
        }

        if (value is not RecordValue { Fields.Count: 4 } record ||
            record.Fields[0] is not I32Value lo ||
            record.Fields[1] is not I32Value mid ||
            record.Fields[2] is not I32Value hi ||
            record.Fields[3] is not I32Value flags)
        {
            throw new NotSupportedException(
                $"Server extension cannot marshal a sandbox value to Decimal wire type '{type}'.");
        }

        result = DecimalFromWire(lo.Value, mid.Value, hi.Value, flags.Value);
        return true;
    }

    private static bool TryDecimalFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (!IsDecimalWireType(type))
        {
            result = null;
            return false;
        }

        value.RequireKind(KernelRpcValueKind.Record);
        if (value.ItemCount != 4)
        {
            throw new NotSupportedException(
                $"Server extension Decimal wire value had {value.ItemCount} fields but expected 4.");
        }

        result = DecimalFromWire(
            value.GetItem(0).Int32Value,
            value.GetItem(1).Int32Value,
            value.GetItem(2).Int32Value,
            value.GetItem(3).Int32Value);
        return true;
    }

    private static SandboxValue DecimalToSandboxValue(decimal value)
    {
        var bits = decimal.GetBits(value);
        return SandboxValue.FromOwnedRecord(
        [
            SandboxValue.FromInt32(bits[0]),
            SandboxValue.FromInt32(bits[1]),
            SandboxValue.FromInt32(bits[2]),
            SandboxValue.FromInt32(bits[3])
        ]);
    }

    private static decimal DecimalFromWire(int lo, int mid, int hi, int flags)
    {
        try
        {
            return new decimal([lo, mid, hi, flags]);
        }
        catch (ArgumentException ex)
        {
            throw new NotSupportedException(
                "Server extension Decimal wire value is not a valid System.Decimal encoding.",
                ex);
        }
    }
}
