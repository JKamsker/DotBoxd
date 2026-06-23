using System.Buffers.Binary;

namespace DotBoxD.Services.Protocol;

internal static class MessageFrameReader
{
    public static void ValidateOutgoingFrame(ReadOnlySpan<byte> frame, int maxMessageSize)
    {
        if (frame.Length < MessageFramer.HeaderSize)
        {
            throw new InvalidDataException($"DotBoxD frame is too small: {frame.Length} bytes.");
        }

        var declaredLength = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(0, 4));
        if (declaredLength != frame.Length)
        {
            throw new InvalidDataException(
                $"DotBoxD frame length prefix {declaredLength} does not match buffer length {frame.Length}.");
        }

        if (declaredLength > maxMessageSize)
        {
            throw new InvalidDataException($"Invalid DotBoxD frame length: {declaredLength}.");
        }
    }

    public static bool TryReadFrame(
        ReadOnlyMemory<byte> source,
        out int messageId,
        out MessageType type,
        out ReadOnlyMemory<byte> envelope,
        out ReadOnlyMemory<byte> payload)
    {
        messageId = 0;
        type = default;
        envelope = ReadOnlyMemory<byte>.Empty;
        payload = ReadOnlyMemory<byte>.Empty;

        if (source.Length < MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize)
        {
            return false;
        }

        var span = source.Span;
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
        if (totalLength < MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize ||
            totalLength != source.Length)
        {
            return false;
        }

        messageId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        type = (MessageType)span[8];

        var envelopeLength = BinaryPrimitives.ReadInt32LittleEndian(
            span.Slice(MessageFramer.HeaderSize, MessageFramer.EnvelopeLengthSize));
        var envelopeStart = MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize;
        if (envelopeLength < 0 || (long)envelopeStart + envelopeLength > totalLength)
        {
            return false;
        }

        envelope = source.Slice(envelopeStart, envelopeLength);
        var payloadStart = envelopeStart + envelopeLength;
        payload = source.Slice(payloadStart, totalLength - payloadStart);
        return true;
    }

    public static bool TryReadFrameHeader(
        ReadOnlyMemory<byte> source,
        out int messageId,
        out MessageType type)
    {
        messageId = 0;
        type = default;

        if (source.Length < MessageFramer.HeaderSize)
        {
            return false;
        }

        var span = source.Span;
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
        if (totalLength < MessageFramer.HeaderSize || totalLength != source.Length)
        {
            return false;
        }

        messageId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        type = (MessageType)span[8];
        return true;
    }
}
