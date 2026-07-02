using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using MessagePack;

namespace DotBoxD.Services.Tests.Support;

internal static class RpcEnvelopeTestFrames
{
    public static byte[] WriteErrorResponseEnvelope(
        MessagePackRpcSerializer serializer,
        int messageId,
        bool isSuccess,
        string errorMessage,
        string errorType,
        RpcStreamHandle? stream)
    {
        var writer = new ArrayBufferWriter<byte>();
        var message = new MessagePackWriter(writer);
        message.WriteMapHeader(5);
        message.Write("MessageId");
        message.Write(messageId);
        message.Write("IsSuccess");
        message.Write(isSuccess);
        message.Write("ErrorMessage");
        message.Write(errorMessage);
        message.Write("ErrorType");
        message.Write(errorType);
        message.Write("Stream");
        WriteStreamHandle(ref message, serializer, stream);
        message.Flush();
        return writer.WrittenMemory.ToArray();
    }

    public static Payload FrameErrorResponse(
        MessagePackRpcSerializer serializer,
        int frameMessageId,
        MessageType messageType,
        int envelopeMessageId,
        bool isSuccess,
        string errorMessage,
        string errorType,
        RpcStreamHandle? stream,
        ReadOnlySpan<byte> trailingPayload = default)
    {
        var envelope = WriteErrorResponseEnvelope(
            serializer,
            envelopeMessageId,
            isSuccess,
            errorMessage,
            errorType,
            stream);
        var body = new byte[MessageFramer.EnvelopeLengthSize + envelope.Length + trailingPayload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, MessageFramer.EnvelopeLengthSize), envelope.Length);
        envelope.CopyTo(body.AsSpan(MessageFramer.EnvelopeLengthSize));
        trailingPayload.CopyTo(body.AsSpan(MessageFramer.EnvelopeLengthSize + envelope.Length));
        return MessageFramer.FrameToPayload(frameMessageId, messageType, body);
    }

    public static void WriteDeeplyNestedArrays(ref MessagePackWriter message, int depth)
    {
        for (var i = 0; i < depth; i++)
        {
            message.WriteArrayHeader(1);
        }

        message.WriteNil();
    }

    private static void WriteStreamHandle(
        ref MessagePackWriter message,
        MessagePackRpcSerializer serializer,
        RpcStreamHandle? stream)
    {
        if (stream is { } handle)
        {
            MessagePackSerializer.Serialize(ref message, handle, serializer.Options);
            return;
        }

        message.WriteNil();
    }
}
