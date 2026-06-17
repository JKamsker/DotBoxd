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
    private const int MaxDecodeDepth = 64;
    private const int MaxDecodeItems = 10_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

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
        reader.ReserveItems(count);
        var values = new KernelRpcValue[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadValue(ref reader, 0);
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
        var value = ReadValue(ref reader, 0);
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
                WriteItems(stream, value.ItemSpan);
                return;
            default:
                throw new NotSupportedException($"Server extension value kind '{value.Kind}' is not supported.");
        }
    }

    private static KernelRpcValue ReadValue(ref Reader reader, int depth)
    {
        var kind = (KernelRpcValueKind)reader.ReadByte();
        return kind switch
        {
            KernelRpcValueKind.Unit => KernelRpcValue.Unit(),
            KernelRpcValueKind.Bool => ReadBool(ref reader),
            KernelRpcValueKind.I32 => KernelRpcValue.Int32(reader.ReadInt32()),
            KernelRpcValueKind.I64 => KernelRpcValue.Int64(reader.ReadInt64()),
            KernelRpcValueKind.F64 => ReadDouble(ref reader),
            KernelRpcValueKind.String => KernelRpcValue.String(reader.ReadString()),
            KernelRpcValueKind.List => KernelRpcValue.List(ReadItems(ref reader, depth)),
            KernelRpcValueKind.Record => KernelRpcValue.Record(ReadItems(ref reader, depth)),
            _ => throw new FormatException($"Server extension payload contains unknown value kind '{kind}'.")
        };
    }

    private static KernelRpcValue ReadBool(ref Reader reader)
    {
        var value = reader.ReadByte();
        return value switch
        {
            0 => KernelRpcValue.Bool(false),
            1 => KernelRpcValue.Bool(true),
            _ => throw new FormatException("Server extension payload contains an invalid bool value.")
        };
    }

    private static KernelRpcValue ReadDouble(ref Reader reader)
    {
        var value = BitConverter.Int64BitsToDouble(reader.ReadInt64());
        if (!double.IsFinite(value))
        {
            throw new FormatException("Server extension payload contains a non-finite F64 value.");
        }

        return KernelRpcValue.Double(value);
    }

    private static void WriteItems(MemoryStream stream, ReadOnlySpan<KernelRpcValue> items)
    {
        WriteLength(stream, items.Length);
        foreach (var item in items)
        {
            WriteValue(stream, item);
        }
    }

    private static KernelRpcValue[] ReadItems(ref Reader reader, int depth)
    {
        var nextDepth = depth + 1;
        if (nextDepth > MaxDecodeDepth)
        {
            throw new FormatException("Server extension payload exceeds the maximum nesting depth.");
        }

        var count = reader.ReadLength();
        reader.ReserveItems(count);
        var values = new KernelRpcValue[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = ReadValue(ref reader, nextDepth);
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
        private int _items;

        public Reader(ReadOnlySpan<byte> payload)
        {
            _remaining = payload;
            _items = 0;
        }

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
            ulong result = 0;
            var shift = 0;
            while (shift < 35)
            {
                var next = ReadByte();
                result |= (ulong)(next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                    {
                        throw new FormatException("Server extension payload contains an invalid length prefix.");
                    }

                    return (int)result;
                }

                shift += 7;
            }

            throw new FormatException("Server extension payload contains an invalid length prefix.");
        }

        public string ReadString()
        {
            var length = ReadLength();
            try
            {
                return StrictUtf8.GetString(Read(length));
            }
            catch (DecoderFallbackException ex)
            {
                throw new FormatException("Server extension payload contains invalid UTF-8.", ex);
            }
        }

        public void ReserveItems(int count)
        {
            if (count < 0 || _items > MaxDecodeItems - count)
            {
                throw new FormatException("Server extension payload contains too many items.");
            }

            _items += count;
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
