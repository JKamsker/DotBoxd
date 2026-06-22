using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Coverage.Core.SerializationCoverageTestSupport;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class SerializationFormatterFailureCoverageTests
{
    [Fact]
    public void RoundTrip_ReadOnlyMemoryBytes_RoundTripsBinaryPayload()
    {
        var serializer = new MessagePackRpcSerializer();
        ReadOnlyMemory<byte> data = new byte[] { 9, 8, 7, 6, 5 };

        var result = RoundTrip(serializer, data);

        Assert.Equal(new byte[] { 9, 8, 7, 6, 5 }, result.ToArray());
    }

    [Fact]
    public void Deserialize_NilIntoReadOnlyMemoryBytes_ReturnsEmpty()
    {
        var serializer = new MessagePackRpcSerializer();
        var nil = new byte[] { 0xc0 };

        var result = serializer.Deserialize<ReadOnlyMemory<byte>>(nil);

        Assert.True(result.IsEmpty);
        Assert.Equal(0, result.Length);
    }

    [Fact]
    public void RoundTrip_EmptyReadOnlyMemoryBytes_ReturnsEmpty()
    {
        var serializer = new MessagePackRpcSerializer();

        var result = RoundTrip(serializer, ReadOnlyMemory<byte>.Empty);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void RoundTrip_ModelWithBinaryField_PreservesBytes()
    {
        var serializer = new MessagePackRpcSerializer();
        var dto = new BinaryFieldDto { Tag = "blob", Data = new byte[] { 1, 2, 3, 4, 5 } };

        var result = RoundTrip(serializer, dto);

        Assert.Equal("blob", result.Tag);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, result.Data.ToArray());
    }

    [Fact]
    public void Deserialize_Truncated_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, SamplePlayerState());

        var full = writer.WrittenMemory.ToArray();
        var truncated = full.AsMemory(0, full.Length / 2);

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(truncated));
    }

    [Fact]
    public void Deserialize_TypeMismatch_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, "not-an-object");

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(writer.WrittenMemory));
    }

    [Fact]
    public void Deserialize_NonGenericTypeMismatch_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, "scalar");

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize(writer.WrittenMemory, typeof(PlayerState)));
    }

    [Fact]
    public void Deserialize_GarbageBytes_ThrowsMessagePackSerializationException()
    {
        var serializer = new MessagePackRpcSerializer();
        var garbage = new byte[] { 0xc1, 0xff, 0xff, 0xff };

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(garbage));
    }

    [Fact]
    public void Deserialize_EmptyBuffer_Throws()
    {
        var serializer = new MessagePackRpcSerializer();

        Assert.ThrowsAny<MessagePackSerializationException>(
            () => serializer.Deserialize<PlayerState>(ReadOnlyMemory<byte>.Empty));
    }
}
