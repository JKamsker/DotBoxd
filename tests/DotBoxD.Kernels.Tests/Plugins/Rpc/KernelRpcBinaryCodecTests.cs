using System.Buffers.Binary;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class KernelRpcBinaryCodecTests
{
    private const int MaxDecodeDepth = 64;
    private const int MaxDecodeItems = 10_000;

    [Fact]
    public void DecodeValue_rejects_length_prefix_that_overflows_int()
    {
        var payload = new byte[]
        {
            (byte)KernelRpcValueKind.String,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0x08
        };

        var ex = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(payload));
        Assert.Contains("invalid length prefix", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeValue_allows_payload_at_maximum_depth()
    {
        var value = KernelRpcBinaryCodec.DecodeValue(NestedListPayload(MaxDecodeDepth));

        for (var i = 0; i < MaxDecodeDepth; i++)
        {
            value.RequireKind(KernelRpcValueKind.List);
            value = Assert.Single(value.Items);
        }

        value.RequireKind(KernelRpcValueKind.Unit);
    }

    [Fact]
    public void DecodeValue_rejects_payload_past_maximum_depth()
    {
        var ex = Assert.Throws<FormatException>(
            () => KernelRpcBinaryCodec.DecodeValue(NestedListPayload(MaxDecodeDepth + 1)));

        Assert.Contains("nesting depth", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeArguments_rejects_excessive_argument_count()
    {
        var ex = Assert.Throws<FormatException>(
            () => KernelRpcBinaryCodec.DecodeArguments(LengthPrefix(MaxDecodeItems + 1)));

        Assert.Contains("too many items", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeArguments_rejects_excessive_aggregate_item_count()
    {
        var payload = new List<byte>();
        payload.AddRange(LengthPrefix(MaxDecodeItems));
        payload.Add((byte)KernelRpcValueKind.List);
        payload.Add(1);
        payload.Add((byte)KernelRpcValueKind.Unit);
        for (var i = 1; i < MaxDecodeItems; i++)
        {
            payload.Add((byte)KernelRpcValueKind.Unit);
        }

        var ex = Assert.Throws<FormatException>(
            () => KernelRpcBinaryCodec.DecodeArguments(payload.ToArray()));

        Assert.Contains("too many items", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeValue_rejects_excessive_nested_item_count()
    {
        var payload = new List<byte> { (byte)KernelRpcValueKind.List };
        payload.AddRange(LengthPrefix(MaxDecodeItems + 1));

        var ex = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(payload.ToArray()));

        Assert.Contains("too many items", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeValue_rejects_bool_byte_other_than_zero_or_one()
    {
        var payload = new byte[] { (byte)KernelRpcValueKind.Bool, 2 };

        var ex = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(payload));

        Assert.Contains("bool", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecodeValue_rejects_invalid_utf8_string()
    {
        var payload = new byte[]
        {
            (byte)KernelRpcValueKind.String,
            2,
            0xC3,
            0x28
        };

        var ex = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(payload));

        Assert.Contains("UTF-8", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DecodeValue_rejects_non_finite_f64(double value)
    {
        var ex = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(F64Payload(value)));

        Assert.Contains("finite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] NestedListPayload(int depth)
    {
        var bytes = new List<byte>((depth * 2) + 1);
        for (var i = 0; i < depth; i++)
        {
            bytes.Add((byte)KernelRpcValueKind.List);
            bytes.Add(1);
        }

        bytes.Add((byte)KernelRpcValueKind.Unit);
        return bytes.ToArray();
    }

    private static byte[] LengthPrefix(int value)
    {
        var bytes = new List<byte>();
        var remaining = (uint)value;
        while (remaining >= 0x80)
        {
            bytes.Add((byte)((remaining & 0x7F) | 0x80));
            remaining >>= 7;
        }

        bytes.Add((byte)remaining);
        return bytes.ToArray();
    }

    private static byte[] F64Payload(double value)
    {
        var payload = new byte[sizeof(byte) + sizeof(long)];
        payload[0] = (byte)KernelRpcValueKind.F64;
        BinaryPrimitives.WriteInt64LittleEndian(
            payload.AsSpan(1),
            BitConverter.DoubleToInt64Bits(value));
        return payload;
    }
}
