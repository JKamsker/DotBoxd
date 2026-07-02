using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Protocol;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class RpcRequestEnvelopeValidationTests
{
    [Theory]
    [MemberData(nameof(RpcRequestsWithNullRequiredNames))]
    public void RpcRequest_NullRequiredNames_ThrowOnSerialize(RpcRequest request, string missingName)
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();

        var ex = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, request));

        Assert.Contains(missingName, ex.ToString());
    }

    [Theory]
    [InlineData(false, false, true, false, "ServiceName")]
    [InlineData(true, true, true, false, "ServiceName")]
    [InlineData(true, false, false, false, "MethodName")]
    [InlineData(true, false, true, true, "MethodName")]
    public void RpcRequest_MissingRequiredNames_Throws(
        bool includeServiceName,
        bool nilServiceName,
        bool includeMethodName,
        bool nilMethodName,
        string missingName)
    {
        var serializer = new MessagePackRpcSerializer();
        var writer = new ArrayBufferWriter<byte>();
        WriteRequestEnvelope(writer, includeServiceName, nilServiceName, includeMethodName, nilMethodName);

        var ex = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Deserialize<RpcRequest>(writer.WrittenMemory));
        Assert.Contains(missingName, ex.ToString());
    }

    public static TheoryData<RpcRequest, string> RpcRequestsWithNullRequiredNames()
        => new()
        {
            { default, "ServiceName" },
            { new RpcRequest { MessageId = 42, ServiceName = null!, MethodName = "Op" }, "ServiceName" },
            { new RpcRequest { MessageId = 42, ServiceName = "Svc", MethodName = null! }, "MethodName" },
        };

    private static void WriteRequestEnvelope(
        IBufferWriter<byte> writer,
        bool includeServiceName,
        bool nilServiceName,
        bool includeMethodName,
        bool nilMethodName)
    {
        var messagePackWriter = new MessagePackWriter(writer);
        var fieldCount = 1 + (includeServiceName ? 1 : 0) + (includeMethodName ? 1 : 0);
        messagePackWriter.WriteMapHeader(fieldCount);
        messagePackWriter.Write("MessageId");
        messagePackWriter.Write(42);

        WriteOptionalName(ref messagePackWriter, "ServiceName", includeServiceName, nilServiceName, "Svc");
        WriteOptionalName(ref messagePackWriter, "MethodName", includeMethodName, nilMethodName, "Op");
        messagePackWriter.Flush();
    }

    private static void WriteOptionalName(
        ref MessagePackWriter writer,
        string fieldName,
        bool include,
        bool nil,
        string value)
    {
        if (!include)
        {
            return;
        }

        writer.Write(fieldName);
        if (nil)
        {
            writer.WriteNil();
        }
        else
        {
            writer.Write(value);
        }
    }
}
