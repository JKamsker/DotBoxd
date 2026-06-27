using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static bool IsDateTimeWireType(Type type)
        => type == typeof(DateTime) || type == typeof(DateTimeOffset);

    private static SandboxType DateTimeWireSandboxType()
        => SandboxType.Record([SandboxType.I64, SandboxType.I64]);

    private static bool TryDateTimeToSandboxValue(object value, Type type, out SandboxValue sandbox)
    {
        DateTimeOffset dateTime;
        if (type == typeof(DateTimeOffset))
        {
            dateTime = (DateTimeOffset)value;
        }
        else if (type == typeof(DateTime))
        {
            dateTime = DateTimeToOffset((DateTime)value);
        }
        else
        {
            sandbox = null!;
            return false;
        }

        sandbox = SandboxValue.FromOwnedRecord(
        [
            SandboxValue.FromInt64(dateTime.UtcTicks),
            SandboxValue.FromInt64(dateTime.Offset.Ticks)
        ]);
        return true;
    }

    private static bool TryDateTimeFromSandboxValue(SandboxValue value, Type type, out object? result)
    {
        if (!IsDateTimeWireType(type))
        {
            result = null;
            return false;
        }

        if (value is not RecordValue { Fields.Count: 2 } record ||
            record.Fields[0] is not I64Value utcTicks ||
            record.Fields[1] is not I64Value offsetTicks)
        {
            throw new NotSupportedException(
                $"Server extension cannot marshal a sandbox value to DateTime wire type '{type}'.");
        }

        result = DateTimeFromOffset(DateTimeOffsetFromWire(utcTicks.Value, offsetTicks.Value), type);
        return true;
    }

    private static bool TryDateTimeFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (!IsDateTimeWireType(type))
        {
            result = null;
            return false;
        }

        value.RequireKind(KernelRpcValueKind.Record);
        if (value.ItemCount != 2)
        {
            throw new NotSupportedException(
                $"Server extension DateTime wire value had {value.ItemCount} fields but expected 2.");
        }

        var dateTime = DateTimeOffsetFromWire(
            value.GetItem(0).Int64Value,
            value.GetItem(1).Int64Value);
        result = DateTimeFromOffset(dateTime, type);
        return true;
    }

    private static DateTimeOffset DateTimeToOffset(DateTime value)
        => value.Kind == DateTimeKind.Local
            ? new DateTimeOffset(value)
            : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), TimeSpan.Zero);

    private static object DateTimeFromOffset(DateTimeOffset value, Type type)
    {
        if (type == typeof(DateTimeOffset))
        {
            return value;
        }

        return value.DateTime;
    }

    private static DateTimeOffset DateTimeOffsetFromWire(long utcTicks, long offsetTicks)
    {
        try
        {
            var offset = TimeSpan.FromTicks(offsetTicks);
            return new DateTimeOffset(checked(utcTicks + offsetTicks), offset);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new NotSupportedException(
                "Server extension DateTime wire value is outside the supported DateTimeOffset range.",
                ex);
        }
    }
}
