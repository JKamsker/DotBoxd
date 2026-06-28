using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private static bool IsFrameworkStructWireType(Type type)
        => type == typeof(DateOnly) ||
           type == typeof(TimeOnly) ||
           type == typeof(Index) ||
           type == typeof(Range);

    private static SandboxType IndexWireSandboxType()
        => SandboxType.Record([SandboxType.I32, SandboxType.Bool]);

    private static SandboxType RangeWireSandboxType()
        => SandboxType.Record([IndexWireSandboxType(), IndexWireSandboxType()]);

    private static bool TryFrameworkStructToSandboxValue(object value, Type type, out SandboxValue sandbox)
    {
        if (type == typeof(Index))
        {
            sandbox = IndexToSandboxValue((Index)value);
            return true;
        }

        if (type == typeof(Range))
        {
            var range = (Range)value;
            sandbox = SandboxValue.FromOwnedRecord(
            [
                IndexToSandboxValue(range.Start),
                IndexToSandboxValue(range.End)
            ]);
            return true;
        }

        sandbox = null!;
        return false;
    }

    private static bool TryFrameworkStructFromSandboxValue(SandboxValue value, Type type, out object? result)
    {
        if (type == typeof(Index))
        {
            result = IndexFromSandboxValue(value);
            return true;
        }

        if (type == typeof(Range))
        {
            result = RangeFromSandboxValue(value);
            return true;
        }

        result = null;
        return false;
    }

    private static bool TryFrameworkStructFromKernelRpcValue(KernelRpcValue value, Type type, out object? result)
    {
        if (type == typeof(Index))
        {
            result = IndexFromKernelRpcValue(value);
            return true;
        }

        if (type == typeof(Range))
        {
            result = RangeFromKernelRpcValue(value);
            return true;
        }

        result = null;
        return false;
    }

    private static DateOnly DateOnlyFromDayNumber(int dayNumber)
    {
        try
        {
            return DateOnly.FromDayNumber(dayNumber);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new NotSupportedException(
                "Server extension DateOnly wire value is outside the supported DateOnly range.",
                ex);
        }
    }

    private static TimeOnly TimeOnlyFromTicks(long ticks)
    {
        try
        {
            return new TimeOnly(ticks);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new NotSupportedException(
                "Server extension TimeOnly wire value is outside the supported TimeOnly range.",
                ex);
        }
    }

    private static SandboxValue IndexToSandboxValue(Index value)
        => SandboxValue.FromOwnedRecord(
        [
            SandboxValue.FromInt32(value.Value),
            SandboxValue.FromBool(value.IsFromEnd)
        ]);

    private static Index IndexFromSandboxValue(SandboxValue value)
    {
        if (value is not RecordValue { Fields.Count: 2 } record ||
            record.Fields[0] is not I32Value indexValue ||
            record.Fields[1] is not BoolValue isFromEnd)
        {
            throw new NotSupportedException(
                "Server extension cannot marshal a sandbox value to Index.");
        }

        return IndexFromWire(indexValue.Value, isFromEnd.Value);
    }

    private static Range RangeFromSandboxValue(SandboxValue value)
    {
        if (value is not RecordValue { Fields.Count: 2 } record)
        {
            throw new NotSupportedException(
                "Server extension cannot marshal a sandbox value to Range.");
        }

        return new Range(
            IndexFromSandboxValue(record.Fields[0]),
            IndexFromSandboxValue(record.Fields[1]));
    }

    private static Index IndexFromKernelRpcValue(KernelRpcValue value)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        if (value.ItemCount != 2)
        {
            throw new NotSupportedException(
                $"Server extension Index wire value had {value.ItemCount} fields but expected 2.");
        }

        return IndexFromWire(value.GetItem(0).Int32Value, value.GetItem(1).BoolValue);
    }

    private static Range RangeFromKernelRpcValue(KernelRpcValue value)
    {
        value.RequireKind(KernelRpcValueKind.Record);
        if (value.ItemCount != 2)
        {
            throw new NotSupportedException(
                $"Server extension Range wire value had {value.ItemCount} fields but expected 2.");
        }

        return new Range(
            IndexFromKernelRpcValue(value.GetItem(0)),
            IndexFromKernelRpcValue(value.GetItem(1)));
    }

    private static Index IndexFromWire(int value, bool isFromEnd)
    {
        try
        {
            return new Index(value, isFromEnd);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new NotSupportedException(
                "Server extension Index wire value is outside the supported Index range.",
                ex);
        }
    }
}
