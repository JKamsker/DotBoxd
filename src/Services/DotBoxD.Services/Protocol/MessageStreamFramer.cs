using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Protocol;

internal static class MessageStreamFramer
{
    public static async Task<MessageFramer.FramedMessage?> ReadMessageAsync(
        Stream stream,
        CancellationToken ct)
    {
        var headerBuffer = ArrayPool<byte>.Shared.Rent(MessageFramer.HeaderSize);
        try
        {
            var bytesRead = await ReadExactAsync(
                stream,
                headerBuffer.AsMemory(0, MessageFramer.HeaderSize),
                ct).ConfigureAwait(false);
            if (bytesRead < MessageFramer.HeaderSize)
            {
                return null;
            }

            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(0, 4));
            if (totalLength < MessageFramer.HeaderSize || totalLength > MessageFramer.MaxMessageSize)
            {
                throw new InvalidDataException($"Invalid DotBoxD frame length: {totalLength}.");
            }

            var messageId = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer.AsSpan(4, 4));
            var messageType = (MessageType)headerBuffer[8];
            var payload = await ReadPayloadAsync(stream, totalLength, ct).ConfigureAwait(false);
            return new MessageFramer.FramedMessage(messageId, messageType, payload);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    public static async Task WriteMessageAsync(
        Stream stream,
        int messageId,
        MessageType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct)
    {
        using var writer = PooledBufferWriter.Rent(MessageFramer.HeaderSize + payload.Length);
        MessageFramer.WriteFrame(writer, messageId, type, payload.Span);
        await stream.WriteAsync(writer.WrittenMemory, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<Payload> ReadPayloadAsync(Stream stream, int totalLength, CancellationToken ct)
    {
        var payloadLength = totalLength - MessageFramer.HeaderSize;
        var payload = Payload.Rent(payloadLength);

        if (payloadLength == 0)
        {
            return payload;
        }

        try
        {
            var bytesRead = await ReadExactAsync(stream, payload.Memory, ct).ConfigureAwait(false);
            if (bytesRead < payloadLength)
            {
                payload.Dispose();
                throw new InvalidDataException(
                    $"Connection closed after {bytesRead} of {payloadLength} payload bytes.");
            }
        }
        catch
        {
            payload.Dispose();
            throw;
        }

        return payload;
    }

    private static async Task<int> ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }
}
