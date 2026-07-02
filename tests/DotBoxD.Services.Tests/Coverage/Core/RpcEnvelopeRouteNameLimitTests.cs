using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RpcEnvelopeRouteNameLimitTests
{
    private const int MaxRouteNameBytes = 256;

    [Theory]
    [InlineData("ServiceName")]
    [InlineData("MethodName")]
    public void RpcRequest_route_name_at_byte_limit_deserializes(string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var nameBytes = CreateAsciiBytes(MaxRouteNameBytes);
        var payload = WriteRequestWithRouteName(fieldName, nameBytes);

        var request = serializer.Deserialize<RpcRequest>(payload);

        Assert.Equal(new string('a', MaxRouteNameBytes), fieldName == "ServiceName"
            ? request.ServiceName
            : request.MethodName);
    }

    [Theory]
    [InlineData("ServiceName")]
    [InlineData("MethodName")]
    public void RpcRequest_oversized_route_name_throws_before_allocating_name(string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var payload = WriteRequestWithRouteName(fieldName, CreateAsciiBytes(1_048_577));
        _ = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(
                WriteRequestWithRouteName(fieldName, CreateAsciiBytes(MaxRouteNameBytes + 1))));

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var ex = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(payload));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains(fieldName, ex.ToString(), StringComparison.Ordinal);
        Assert.True(allocated < 256_000, $"oversized route-name rejection allocated {allocated} bytes");
    }

    [Theory]
    [InlineData("ServiceName")]
    [InlineData("MethodName")]
    public void RpcRequest_oversized_route_name_is_not_serialized(string fieldName)
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest
        {
            MessageId = 42,
            ServiceName = fieldName == "ServiceName" ? new string('a', MaxRouteNameBytes + 1) : "Sample.Service",
            MethodName = fieldName == "MethodName" ? new string('a', MaxRouteNameBytes + 1) : "Call",
            Streams = []
        };

        var writer = new ArrayBufferWriter<byte>();
        var ex = Assert.Throws<MessagePackSerializationException>(() => serializer.Serialize(writer, request));

        Assert.Contains(fieldName, ex.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, writer.WrittenCount);
    }

    private static byte[] WriteRequestWithRouteName(string fieldName, byte[] nameBytes)
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(3);
        message.Write("MessageId");
        message.Write(42);
        message.Write("ServiceName");
        WriteRouteName(ref message, fieldName == "ServiceName" ? nameBytes : "Sample.Service"u8);
        message.Write("MethodName");
        WriteRouteName(ref message, fieldName == "MethodName" ? nameBytes : "Call"u8);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    private static void WriteRouteName(ref MessagePackWriter message, ReadOnlySpan<byte> utf8)
    {
        message.WriteStringHeader(utf8.Length);
        message.WriteRaw(utf8);
    }

    private static byte[] CreateAsciiBytes(int count)
    {
        var bytes = new byte[count];
        Array.Fill(bytes, (byte)'a');
        return bytes;
    }
}
