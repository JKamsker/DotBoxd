namespace DotBoxD.Plugins;

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using DotBoxD.Plugins.Runtime.Rpc;

/// <summary>
/// Encodes server extension IR values as a small binary payload: one value-kind byte followed by only the
/// active scalar or child sequence. The IPC layer transports the resulting bytes as an ordinary binary
/// argument, avoiding reflection-bound DTO maps and repeated string kind tags on the wire.
/// </summary>
/// <remarks>
/// The encode side writes into an <see cref="IBufferWriter{T}"/> so the hot push path can supply a pooled
/// buffer and hand its written span straight to the transport. The <c>byte[]</c>-returning overloads stay for
/// server-extension callers and tests; they encode through a pooled writer and copy once at the boundary.
/// </remarks>
public static class KernelRpcBinaryCodec
{
    private const int MaxDecodeDepth = 64;
    private const int MaxDecodeItems = 10_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] EncodeArguments(IReadOnlyList<KernelRpcValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using var writer = PooledRpcBufferWriter.Rent();
        EncodeArguments(arguments, writer);
        return writer.WrittenMemory.ToArray();
    }

    /// <summary>Encodes <paramref name="arguments"/> into <paramref name="writer"/> without an intermediate array.</summary>
    public static void EncodeArguments(IReadOnlyList<KernelRpcValue> arguments, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(writer);
        WriteLength(writer, arguments.Count);
        for (var i = 0; i < arguments.Count; i++)
        {
            WriteValue(writer, arguments[i]);
        }
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
        using var writer = PooledRpcBufferWriter.Rent();
        EncodeValue(value, writer);
        return writer.WrittenMemory.ToArray();
    }

    /// <summary>Encodes <paramref name="value"/> into <paramref name="writer"/> without an intermediate array.</summary>
    public static void EncodeValue(KernelRpcValue value, IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        WriteValue(writer, value);
    }

    public static KernelRpcValue DecodeValue(ReadOnlyMemory<byte> payload)
    {
        var reader = new Reader(payload.Span);
        var value = ReadValue(ref reader, 0);
        reader.EnsureConsumed();
        return value;
    }

    private static void WriteValue(IBufferWriter<byte> writer, KernelRpcValue value)
    {
        WriteByte(writer, (byte)value.Kind);
        switch (value.Kind)
        {
            case KernelRpcValueKind.Unit:
                return;
            case KernelRpcValueKind.Bool:
                WriteByte(writer, value.BoolValue ? (byte)1 : (byte)0);
                return;
            case KernelRpcValueKind.I32:
                WriteInt32(writer, value.Int32Value);
                return;
            case KernelRpcValueKind.I64:
                WriteInt64(writer, value.Int64Value);
                return;
            case KernelRpcValueKind.F64:
                WriteInt64(writer, BitConverter.DoubleToInt64Bits(value.DoubleValue));
                return;
            case KernelRpcValueKind.String:
                WriteString(writer, value.TextValue);
                return;
            case KernelRpcValueKind.List:
            case KernelRpcValueKind.Record:
            case KernelRpcValueKind.Map:
                WriteItems(writer, value.ItemSpan);
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
            KernelRpcValueKind.List => KernelRpcValue.ListFromOwnedItems(ReadItems(ref reader, depth)),
            KernelRpcValueKind.Record => KernelRpcValue.RecordFromOwnedFields(ReadItems(ref reader, depth)),
            KernelRpcValueKind.Map => KernelRpcValue.MapFromOwnedEntries(ReadItems(ref reader, depth)),
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

    private static void WriteItems(IBufferWriter<byte> writer, ReadOnlySpan<KernelRpcValue> items)
    {
        WriteLength(writer, items.Length);
        foreach (var item in items)
        {
            WriteValue(writer, item);
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

    // Writes the UTF-8 bytes straight into the writer's span: GetByteCount sizes the rent and GetSpan/GetBytes
    // fills it in place, so there is no per-string transient byte[] (the array the old MemoryStream path leaked).
    private static void WriteString(IBufferWriter<byte> writer, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteLength(writer, byteCount);
        if (byteCount == 0)
        {
            return;
        }

        var span = writer.GetSpan(byteCount);
        var written = Encoding.UTF8.GetBytes(value, span);
        writer.Advance(written);
    }

    private static void WriteInt32(IBufferWriter<byte> writer, int value)
    {
        var span = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        writer.Advance(sizeof(int));
    }

    private static void WriteInt64(IBufferWriter<byte> writer, long value)
    {
        var span = writer.GetSpan(sizeof(long));
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        writer.Advance(sizeof(long));
    }

    private static void WriteLength(IBufferWriter<byte> writer, int value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Server extension payload lengths must be non-negative.");
        }

        var remaining = (uint)value;
        while (remaining >= 0x80)
        {
            WriteByte(writer, (byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }

        WriteByte(writer, (byte)remaining);
    }

    private static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
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
