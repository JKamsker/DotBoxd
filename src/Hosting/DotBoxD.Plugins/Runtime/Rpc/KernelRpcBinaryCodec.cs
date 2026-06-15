namespace DotBoxD.Plugins;

using System.Buffers.Binary;
using System.Text;

/// <summary>
/// Encodes server extension IR values as a small binary payload: one value-kind byte followed by only the
/// active scalar or child sequence. The IPC layer transports the resulting bytes as an ordinary binary
/// argument, avoiding reflection-bound DTO maps and repeated string kind tags on the wire.
/// </summary>
public static class KernelRpcBinaryCodec
{
    public static byte[] EncodeArguments(IReadOnlyList<KernelRpcValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var stream = new MemoryStream();
        WriteLength(stream, arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            WriteValue(stream, arguments[i]);
        }

        return stream.ToArray();
    }

    public static KernelRpcValue[] DecodeArguments(ReadOnlyMemory<byte> payload)
    {
        var reader = new Reader(payload.Span);
        var count = reader.ReadLength();
        var values = new KernelRpcValue[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadValue(ref reader);
        }

        reader.EnsureConsumed();
        return values;
    }

    public static byte[] EncodeValue(KernelRpcValue value)
    {
        var stream = new MemoryStream();
        WriteValue(stream, value);
        return stream.ToArray();
    }

    public static KernelRpcValue DecodeValue(ReadOnlyMemory<byte> payload)
    {
        var reader = new Reader(payload.Span);
        var value = ReadValue(ref reader);
        reader.EnsureConsumed();
        return value;
    }

    private static void WriteValue(MemoryStream stream, KernelRpcValue value)
    {
        stream.WriteByte((byte)value.Kind);
        switch (value.Kind)
        {
            case KernelRpcValueKind.Unit:
                return;
            case KernelRpcValueKind.Bool:
                stream.WriteByte(value.BoolValue ? (byte)1 : (byte)0);
                return;
            case KernelRpcValueKind.I32:
                WriteInt32(stream, value.Int32Value);
                return;
            case KernelRpcValueKind.I64:
                WriteInt64(stream, value.Int64Value);
                return;
            case KernelRpcValueKind.F64:
                WriteInt64(stream, BitConverter.DoubleToInt64Bits(value.DoubleValue));
                return;
            case KernelRpcValueKind.String:
                WriteString(stream, value.TextValue);
                return;
            case KernelRpcValueKind.List:
            case KernelRpcValueKind.Record:
                WriteItems(stream, value.Items);
                return;
            default:
                throw new NotSupportedException($"Server extension value kind '{value.Kind}' is not supported.");
        }
    }

    private static KernelRpcValue ReadValue(ref Reader reader)
    {
        var kind = (KernelRpcValueKind)reader.ReadByte();
        return kind switch
        {
            KernelRpcValueKind.Unit => KernelRpcValue.Unit(),
            KernelRpcValueKind.Bool => KernelRpcValue.Bool(reader.ReadByte() != 0),
            KernelRpcValueKind.I32 => KernelRpcValue.Int32(reader.ReadInt32()),
            KernelRpcValueKind.I64 => KernelRpcValue.Int64(reader.ReadInt64()),
            KernelRpcValueKind.F64 => KernelRpcValue.Double(BitConverter.Int64BitsToDouble(reader.ReadInt64())),
            KernelRpcValueKind.String => KernelRpcValue.String(reader.ReadString()),
            KernelRpcValueKind.List => KernelRpcValue.List(ReadItems(ref reader)),
            KernelRpcValueKind.Record => KernelRpcValue.Record(ReadItems(ref reader)),
            _ => throw new FormatException($"Server extension payload contains unknown value kind '{kind}'.")
        };
    }

    private static void WriteItems(MemoryStream stream, KernelRpcValue[] items)
    {
        WriteLength(stream, items.Length);
        foreach (var item in items)
        {
            WriteValue(stream, item);
        }
    }

    private static KernelRpcValue[] ReadItems(ref Reader reader)
    {
        var count = reader.ReadLength();
        var values = new KernelRpcValue[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadValue(ref reader);
        }

        return values;
    }

    private static void WriteString(MemoryStream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteLength(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteInt32(MemoryStream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64(MemoryStream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteLength(MemoryStream stream, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Server extension payload lengths must be non-negative.");
        }

        var remaining = (uint)value;
        while (remaining >= 0x80)
        {
            stream.WriteByte((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }

        stream.WriteByte((byte)remaining);
    }

    private ref struct Reader
    {
        private ReadOnlySpan<byte> _remaining;

        public Reader(ReadOnlySpan<byte> payload) => _remaining = payload;

        public byte ReadByte()
        {
            if (_remaining.IsEmpty)
            {
                throw new FormatException("Server extension payload ended unexpectedly.");
            }

            var value = _remaining[0];
            _remaining = _remaining[1..];
            return value;
        }

        public int ReadInt32()
        {
            var bytes = Read(sizeof(int));
            return BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        public long ReadInt64()
        {
            var bytes = Read(sizeof(long));
            return BinaryPrimitives.ReadInt64LittleEndian(bytes);
        }

        public int ReadLength()
        {
            var result = 0;
            var shift = 0;
            while (shift < 35)
            {
                var next = ReadByte();
                result |= (next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }

            throw new FormatException("Server extension payload contains an invalid length prefix.");
        }

        public string ReadString()
        {
            var length = ReadLength();
            return Encoding.UTF8.GetString(Read(length));
        }

        public void EnsureConsumed()
        {
            if (!_remaining.IsEmpty)
            {
                throw new FormatException("Server extension payload contains trailing bytes.");
            }
        }

        private ReadOnlySpan<byte> Read(int length)
        {
            if (length < 0 || _remaining.Length < length)
            {
                throw new FormatException("Server extension payload ended unexpectedly.");
            }

            var bytes = _remaining[..length];
            _remaining = _remaining[length..];
            return bytes;
        }
    }
}
