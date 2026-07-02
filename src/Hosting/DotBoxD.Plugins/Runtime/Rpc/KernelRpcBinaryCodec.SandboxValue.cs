namespace DotBoxD.Plugins;

using System.Buffers;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcBinaryCodec
{
    public static byte[] EncodeValue(SandboxValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = PooledRpcBufferWriter.Rent();
        var itemCount = 0;
        WriteSandboxValue(value, writer, 0, ref itemCount);
        return writer.WrittenMemory.ToArray();
    }

    /// <summary>Encodes <paramref name="value"/> into <paramref name="writer"/> without building a KernelRpcValue tree.</summary>
    public static void EncodeValue(SandboxValue value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(writer);
        var itemCount = 0;
        WriteSandboxValue(value, writer, 0, ref itemCount);
    }

    internal static void BeginRecord(int fieldCount, IBufferWriter<byte> writer)
    {
        if (fieldCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldCount));
        }

        ArgumentNullException.ThrowIfNull(writer);
        WriteByte(writer, (byte)KernelRpcValueKind.Record);
        WriteLength(writer, fieldCount);
    }

    internal static void EncodeRecordField(SandboxValue value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(writer);
        var itemCount = 0;
        WriteSandboxValue(value, writer, 0, ref itemCount);
    }

    internal static void EncodeBoolValue(bool value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteByte(writer, (byte)KernelRpcValueKind.Bool);
        WriteByte(writer, value ? (byte)1 : (byte)0);
    }

    internal static void EncodeInt32Value(int value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteByte(writer, (byte)KernelRpcValueKind.I32);
        WriteInt32(writer, value);
    }

    internal static void EncodeInt64Value(long value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteByte(writer, (byte)KernelRpcValueKind.I64);
        WriteInt64(writer, value);
    }

    internal static void EncodeDoubleValue(double value, IBufferWriter<byte> writer)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Kernel RPC F64 values must be finite.");
        }

        ArgumentNullException.ThrowIfNull(writer);
        WriteByte(writer, (byte)KernelRpcValueKind.F64);
        WriteInt64(writer, BitConverter.DoubleToInt64Bits(value));
    }

    internal static void EncodeStringValue(string value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(writer);
        WriteByte(writer, (byte)KernelRpcValueKind.String);
        WriteString(writer, value);
    }

    internal static void EncodeGuidValue(Guid value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteByte(writer, (byte)KernelRpcValueKind.Guid);
        WriteGuid(writer, value);
    }

    private static void WriteSandboxValue(
        SandboxValue value,
        IBufferWriter<byte> writer,
        int depth,
        ref int itemCount)
    {
        switch (value)
        {
            case UnitValue:
                WriteByte(writer, (byte)KernelRpcValueKind.Unit);
                return;
            case BoolValue boolean:
                WriteByte(writer, (byte)KernelRpcValueKind.Bool);
                WriteByte(writer, boolean.Value ? (byte)1 : (byte)0);
                return;
            case I32Value number:
                WriteByte(writer, (byte)KernelRpcValueKind.I32);
                WriteInt32(writer, number.Value);
                return;
            case I64Value number:
                WriteByte(writer, (byte)KernelRpcValueKind.I64);
                WriteInt64(writer, number.Value);
                return;
            case F64Value number:
                if (!double.IsFinite(number.Value))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(value),
                        number.Value,
                        "F64 values must be finite.");
                }

                WriteByte(writer, (byte)KernelRpcValueKind.F64);
                WriteInt64(writer, BitConverter.DoubleToInt64Bits(number.Value));
                return;
            case StringValue text:
                WriteByte(writer, (byte)KernelRpcValueKind.String);
                WriteString(writer, text.Value);
                return;
            case GuidValue guid:
                WriteByte(writer, (byte)KernelRpcValueKind.Guid);
                WriteGuid(writer, guid.Value);
                return;
            case ListValue list:
                var listDepth = EnterEncodeCollection(depth);
                ReserveEncodeItems(list.Values.Count, ref itemCount);
                WriteByte(writer, (byte)KernelRpcValueKind.List);
                WriteListValues(writer, list.Values, listDepth, ref itemCount);
                return;
            case RecordValue record:
                var recordDepth = EnterEncodeCollection(depth);
                ReserveEncodeItems(record.Fields.Count, ref itemCount);
                WriteByte(writer, (byte)KernelRpcValueKind.Record);
                WriteListValues(writer, record.Fields, recordDepth, ref itemCount);
                return;
            case MapValue map:
                var mapDepth = EnterEncodeCollection(depth);
                var mapItemCount = checked(map.Values.Count * 2);
                ReserveEncodeItems(mapItemCount, ref itemCount);
                WriteByte(writer, (byte)KernelRpcValueKind.Map);
                WriteMapValues(writer, map, mapDepth, mapItemCount, ref itemCount);
                return;
            default:
                throw new NotSupportedException(
                    $"Server extension IPC cannot marshal sandbox value '{value.GetType().Name}'.");
        }
    }

    private static void WriteListValues(
        IBufferWriter<byte> writer,
        IReadOnlyList<SandboxValue> values,
        int depth,
        ref int itemCount)
    {
        WriteLength(writer, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            WriteSandboxValue(values[i], writer, depth, ref itemCount);
        }
    }

    private static void WriteMapValues(
        IBufferWriter<byte> writer,
        MapValue values,
        int depth,
        int itemCount,
        ref int aggregateItemCount)
    {
        WriteLength(writer, itemCount);
        foreach (var pair in values.Entries)
        {
            WriteSandboxValue(pair.Key, writer, depth, ref aggregateItemCount);
            WriteSandboxValue(pair.Value, writer, depth, ref aggregateItemCount);
        }
    }
}
