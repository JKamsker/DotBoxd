using System.Buffers.Binary;

namespace DotBoxD.Services.Protocol;

internal static class MessageFrameReader
{
    public static int GetOutgoingFrameLength(int payloadLength)
    {
        var totalLength = (long)MessageFramer.HeaderSize + payloadLength;
        if (totalLength < MessageFramer.HeaderSize || totalLength > MessageFramer.MaxMessageSize)
        {
            throw new InvalidDataException($"Invalid DotBoxD frame length: {totalLength}.");
        }

        return (int)totalLength;
    }

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

        if (!IsDefinedMessageType((MessageType)frame[8]))
        {
            throw new InvalidDataException($"Invalid DotBoxD message type: 0x{frame[8]:X2}.");
        }
    }

    public static void ThrowIfUndefinedMessageType(MessageType type)
    {
        if (!IsDefinedMessageType(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                "Unsupported DotBoxD message type.");
        }
    }

    public static bool IsDefinedMessageType(MessageType type) =>
        type is MessageType.Request or
            MessageType.Response or
            MessageType.Error or
            MessageType.Cancel or
            MessageType.StreamItem or
            MessageType.StreamComplete or
            MessageType.StreamError or
            MessageType.StreamCredit or
            MessageType.StreamCancel;

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
            totalLength != source.Length ||
            totalLength > MessageFramer.MaxMessageSize)
        {
            return false;
        }

        var messageType = (MessageType)span[8];
        if (!IsDefinedMessageType(messageType))
        {
            return false;
        }

        messageId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        type = messageType;

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
        => TryReadFrameHeader(source, validateMessageType: true, out messageId, out type);

    public static bool TryReadFrameHeaderUnchecked(
        ReadOnlyMemory<byte> source,
        out int messageId,
        out MessageType type)
        => TryReadFrameHeader(source, validateMessageType: false, out messageId, out type);

    private static bool TryReadFrameHeader(
        ReadOnlyMemory<byte> source,
        bool validateMessageType,
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
        if (totalLength < MessageFramer.HeaderSize ||
            totalLength != source.Length ||
            totalLength > MessageFramer.MaxMessageSize)
        {
            return false;
        }

        var messageType = (MessageType)span[8];
        if (validateMessageType && !IsDefinedMessageType(messageType))
        {
            return false;
        }

        messageId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
        type = messageType;
        return true;
    }
}
