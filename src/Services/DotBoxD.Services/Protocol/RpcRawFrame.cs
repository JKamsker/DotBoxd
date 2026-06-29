using System.Buffers.Binary;
using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Protocol;

internal static class RpcRawFrame
{
    public static void WritePrefix(PooledBufferWriter writer, int messageId, MessageType type)
    {
        MessageFrameReader.ThrowIfUndefinedMessageType(type);

        var span = writer.GetSpan(MessageFramer.HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)type;
        writer.Advance(MessageFramer.HeaderSize);
    }

    public static Payload Finish(PooledBufferWriter writer)
    {
        Complete(writer);
        var frame = writer.DetachPayload();
        return frame;
    }

    public static void Complete(PooledBufferWriter writer) =>
        BinaryPrimitives.WriteInt32LittleEndian(writer.WrittenSpan.Slice(0, 4), writer.WrittenCount);

    public static PooledBufferWriter RentFrame(int messageId, MessageType type, ReadOnlySpan<byte> payload)
    {
        var writer = PooledBufferWriter.Rent(MessageFramer.HeaderSize + payload.Length);
        try
        {
            WritePrefix(writer, messageId, type);
            if (payload.Length > 0)
            {
                var span = writer.GetSpan(payload.Length);
                payload.CopyTo(span);
                writer.Advance(payload.Length);
            }
            Complete(writer);
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    public static PooledBufferWriter RentInt32Frame(int messageId, MessageType type, int value)
    {
        var writer = PooledBufferWriter.Rent(MessageFramer.HeaderSize + sizeof(int));
        try
        {
            WritePrefix(writer, messageId, type);
            BinaryPrimitives.WriteInt32LittleEndian(writer.GetSpan(sizeof(int)), value);
            writer.Advance(sizeof(int));
            Complete(writer);
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    public static Payload FrameInt32(int messageId, MessageType type, int value)
    {
        MessageFrameReader.ThrowIfUndefinedMessageType(type);

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
