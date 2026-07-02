using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Kernels.Sandbox;
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
    public void Map_value_survives_a_binary_round_trip()
    {
        var payload = KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.Map(
        [
            KernelRpcValue.String("a"),
            KernelRpcValue.Int32(1),
            KernelRpcValue.String("b"),
            KernelRpcValue.Int32(2)
        ]));

        var value = KernelRpcBinaryCodec.DecodeValue(payload);

        value.RequireKind(KernelRpcValueKind.Map);
        Assert.Equal(4, value.ItemCount);
        Assert.Equal("a", value.GetItem(0).TextValue);
        Assert.Equal(1, value.GetItem(1).Int32Value);
        Assert.Equal("b", value.GetItem(2).TextValue);
        Assert.Equal(2, value.GetItem(3).Int32Value);
    }

    [Fact]
    public void EncodeValue_writes_sandbox_values_like_the_kernel_value_route()
    {
        var sandbox = SandboxValue.FromRecord(
        [
            SandboxValue.FromInt32(7),
            SandboxValue.FromString("crypt"),
            SandboxValue.FromList(
                [SandboxValue.FromGuid(Guid.Parse("5e74eabc-3c70-4ff1-9ba7-a08f9d27676d"))],
                SandboxType.Guid),
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("score")] = SandboxValue.FromInt64(9001)
                },
                SandboxType.String,
                SandboxType.I64)
        ]);
        var expected = KernelRpcBinaryCodec.EncodeValue(KernelRpcValueConverter.FromSandboxValue(sandbox));

        var directBytes = KernelRpcBinaryCodec.EncodeValue(sandbox);
        var writer = new ArrayBufferWriter<byte>();
        KernelRpcBinaryCodec.EncodeValue(sandbox, writer);

        Assert.Equal(expected, directBytes);
        Assert.Equal(expected, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void DecodeValue_rejects_a_map_with_an_odd_entry_count()
    {
        var payload = new List<byte> { (byte)KernelRpcValueKind.Map };
        payload.AddRange(LengthPrefix(1));
        payload.Add((byte)KernelRpcValueKind.Unit);

        var ex = Assert.Throws<FormatException>(() => KernelRpcBinaryCodec.DecodeValue(payload.ToArray()));

        Assert.Contains("odd", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void DecodeArguments_reuses_empty_array_for_empty_payload()
    {
        var payload = KernelRpcBinaryCodec.EncodeArguments(Array.Empty<KernelRpcValue>());

        var arguments = KernelRpcBinaryCodec.DecodeArguments(payload);

        Assert.Same(Array.Empty<KernelRpcValue>(), arguments);
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
    public void EncodeArguments_rejects_excessive_argument_count()
    {
        var arguments = new KernelRpcValue[MaxDecodeItems + 1];
        Array.Fill(arguments, KernelRpcValue.Unit());

        var ex = Assert.Throws<ArgumentException>(() => KernelRpcBinaryCodec.EncodeArguments(arguments));

        Assert.Contains("too many items", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeValue_rejects_kernel_value_past_maximum_depth()
    {
        var value = NestedKernelList(MaxDecodeDepth + 1);

        var ex = Assert.Throws<ArgumentException>(() => KernelRpcBinaryCodec.EncodeValue(value));

        Assert.Contains("nesting depth", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeValue_rejects_kernel_value_with_excessive_nested_item_count()
    {
        var items = new KernelRpcValue[MaxDecodeItems + 1];
        Array.Fill(items, KernelRpcValue.Unit());

        var ex = Assert.Throws<ArgumentException>(() => KernelRpcBinaryCodec.EncodeValue(KernelRpcValue.List(items)));

        Assert.Contains("too many items", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeValue_rejects_direct_sandbox_value_past_maximum_depth()
    {
        var value = NestedSandboxList(MaxDecodeDepth + 1);

        var ex = Assert.Throws<ArgumentException>(() => KernelRpcBinaryCodec.EncodeValue(value));

        Assert.Contains("nesting depth", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeValue_rejects_direct_sandbox_value_with_excessive_nested_item_count()
    {
        var items = new SandboxValue[MaxDecodeItems + 1];
        Array.Fill(items, SandboxValue.Unit);
        var value = SandboxValue.FromList(items, SandboxType.Unit);

        var ex = Assert.Throws<ArgumentException>(() => KernelRpcBinaryCodec.EncodeValue(value));

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

    [Fact]
    public void EncodeValue_rejects_strings_with_unpaired_surrogates()
    {
        var text = "prefix-" + new string(new[] { (char)0xD800 }) + "-suffix";
        var kernelValue = KernelRpcValue.String(text);
        var sandboxValue = SandboxValue.FromString(text);

        AssertInvalidUtf16(() => KernelRpcBinaryCodec.EncodeValue(kernelValue));
        AssertInvalidUtf16(() => KernelRpcBinaryCodec.EncodeValue(kernelValue, new ArrayBufferWriter<byte>()));
        AssertInvalidUtf16(() => KernelRpcBinaryCodec.EncodeValue(sandboxValue));
        AssertInvalidUtf16(() => KernelRpcBinaryCodec.EncodeValue(sandboxValue, new ArrayBufferWriter<byte>()));
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

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void EncodeValue_rejects_non_finite_direct_sandbox_f64(double value)
    {
        var sandbox = new F64Value(value);
        var writer = new ArrayBufferWriter<byte>();

        var bytesEx = Assert.Throws<ArgumentOutOfRangeException>(() => KernelRpcBinaryCodec.EncodeValue(sandbox));
        var writerEx = Assert.Throws<ArgumentOutOfRangeException>(() => KernelRpcBinaryCodec.EncodeValue(sandbox, writer));

        Assert.Contains("finite", bytesEx.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finite", writerEx.Message, StringComparison.OrdinalIgnoreCase);
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

    private static KernelRpcValue NestedKernelList(int depth)
    {
        var value = KernelRpcValue.Unit();
        for (var i = 0; i < depth; i++)
        {
            value = KernelRpcValue.List([value]);
        }

        return value;
    }

    private static SandboxValue NestedSandboxList(int depth)
    {
        var value = SandboxValue.Unit;
        for (var i = 0; i < depth; i++)
        {
            value = SandboxValue.FromList([value], value.Type);
        }

        return value;
    }

    private static void AssertInvalidUtf16(Action encode)
    {
        var ex = Assert.Throws<ArgumentException>(encode);
        Assert.Contains("UTF-16", ex.Message, StringComparison.OrdinalIgnoreCase);
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
