namespace DotBoxD.Plugins;

using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;

/// <summary>
/// Low-level reader used by generated plugin code to decode a known <see cref="KernelRpcValue"/> payload shape
/// without first materializing a full <see cref="KernelRpcValue"/> tree.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct KernelRpcPayloadReader
{
    private const int MaxDecodeItems = 10_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private ReadOnlySpan<byte> _remaining;
    private int _items;

    public KernelRpcPayloadReader(ReadOnlySpan<byte> payload)
    {
        _remaining = payload;
        _items = 0;
    }

    public void ReadUnit() => ReadKind(KernelRpcValueKind.Unit);

    public bool ReadBool()
    {
        ReadKind(KernelRpcValueKind.Bool);
        return ReadByte() switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException("Server extension payload contains an invalid bool value.")
        };
    }

    public int ReadInt32()
    {
        ReadKind(KernelRpcValueKind.I32);
        return BinaryPrimitives.ReadInt32LittleEndian(Read(sizeof(int)));
    }

    public long ReadInt64()
    {
        ReadKind(KernelRpcValueKind.I64);
        return BinaryPrimitives.ReadInt64LittleEndian(Read(sizeof(long)));
    }

    public double ReadDouble()
    {
        ReadKind(KernelRpcValueKind.F64);
        var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Read(sizeof(long))));
        if (!double.IsFinite(value))
        {
            throw new FormatException("Server extension payload contains a non-finite F64 value.");
        }

        return value;
    }

    public string ReadString()
    {
        ReadKind(KernelRpcValueKind.String);
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

    public Guid ReadGuid()
    {
        ReadKind(KernelRpcValueKind.Guid);
        return new Guid(Read(16));
    }

    public int ReadListHeader()
    {
        ReadKind(KernelRpcValueKind.List);
        return ReadCount();
    }

    public int ReadRecordHeader()
    {
        ReadKind(KernelRpcValueKind.Record);
        return ReadCount();
    }

    public int ReadMapHeader()
    {
        ReadKind(KernelRpcValueKind.Map);
        var count = ReadCount();
        if ((count & 1) != 0)
        {
            throw new FormatException("Server extension map payload has an odd key/value entry count.");
        }

        return count;
    }

    public void EnsureConsumed()
    {
        if (!_remaining.IsEmpty)
        {
            throw new FormatException("Server extension payload contains trailing bytes.");
        }
    }

    private int ReadCount()
    {
        var count = ReadLength();
        if (count < 0 || _items > MaxDecodeItems - count)
        {
            throw new FormatException("Server extension payload contains too many items.");
        }

        _items += count;
        return count;
    }

    private void ReadKind(KernelRpcValueKind expected)
    {
        var actual = (KernelRpcValueKind)ReadByte();
        if (actual != expected)
        {
            throw new FormatException(
                $"Server extension payload expected '{expected}' but received '{actual}'.");
        }
    }

    private byte ReadByte()
    {
        if (_remaining.IsEmpty)
        {
            throw new FormatException("Server extension payload ended unexpectedly.");
        }

        var value = _remaining[0];
        _remaining = _remaining[1..];
        return value;
    }

    private int ReadLength()
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
