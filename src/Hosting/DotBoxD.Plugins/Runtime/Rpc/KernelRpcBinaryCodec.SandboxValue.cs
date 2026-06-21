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
        EncodeValue(value, writer);
        return writer.WrittenMemory.ToArray();
    }

    /// <summary>Encodes <paramref name="value"/> into <paramref name="writer"/> without building a KernelRpcValue tree.</summary>
    public static void EncodeValue(SandboxValue value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(writer);
        WriteSandboxValue(writer, value);
    }

    private static void WriteSandboxValue(IBufferWriter<byte> writer, SandboxValue value)
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
                WriteByte(writer, (byte)KernelRpcValueKind.List);
                WriteListValues(writer, list.Values);
                return;
            case RecordValue record:
                WriteByte(writer, (byte)KernelRpcValueKind.Record);
                WriteListValues(writer, record.Fields);
                return;
            case MapValue map:
                WriteByte(writer, (byte)KernelRpcValueKind.Map);
                WriteMapValues(writer, map.Values);
                return;
            default:
                throw new NotSupportedException(
                    $"Server extension IPC cannot marshal sandbox value '{value.GetType().Name}'.");
        }
    }

    private static void WriteListValues(IBufferWriter<byte> writer, IReadOnlyList<SandboxValue> values)
    {
        WriteLength(writer, values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            WriteSandboxValue(writer, values[i]);
        }
    }

    private static void WriteMapValues(IBufferWriter<byte> writer, IReadOnlyDictionary<SandboxValue, SandboxValue> values)
    {
        WriteLength(writer, checked(values.Count * 2));
        foreach (var pair in values)
        {
            WriteSandboxValue(writer, pair.Key);
            WriteSandboxValue(writer, pair.Value);
        }
    }
}
