using System.Buffers.Binary;
using DotBoxd.Services.Buffers;

namespace DotBoxd.Services.Protocol;

internal static class RpcRawFrame
{
    public static void WritePrefix(PooledBufferWriter writer, int messageId, MessageType type)
    {
        var span = writer.GetSpan(MessageFramer.HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)type;
        writer.Advance(MessageFramer.HeaderSize);
    }

    public static Payload Finish(PooledBufferWriter writer)
    {
        var frame = writer.DetachPayload();
        BinaryPrimitives.WriteInt32LittleEndian(frame.Memory.Span.Slice(0, 4), frame.Length);
        return frame;
    }

    public static Payload FrameInt32(int messageId, MessageType type, int value)
    {
        var frame = Payload.Rent(MessageFramer.HeaderSize + sizeof(int));
        var span = frame.Memory.Span;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(MessageFramer.HeaderSize, sizeof(int)), value);
        return frame;
    }

    public static bool TryReadInt32(ReadOnlyMemory<byte> frame, out int value)
    {
        value = 0;
        if (frame.Length != MessageFramer.HeaderSize + sizeof(int))
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(frame.Span.Slice(MessageFramer.HeaderSize, sizeof(int)));
        return true;
    }
}
