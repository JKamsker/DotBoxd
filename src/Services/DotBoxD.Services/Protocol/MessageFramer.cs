using System.Buffers;
using System.Buffers.Binary;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Serialization;

namespace DotBoxD.Services.Protocol;

/// <summary>
/// Handles message framing for the DotBoxD protocol.
/// Stream frame: [4 bytes: Total Length][4 bytes: MessageId][1 byte: MessageType][N bytes: Body].
/// For RPC messages the body is un-nested as [4 bytes: Envelope Length][E bytes: Envelope][P bytes: Payload],
/// so the trailing payload can be handed to callers as a zero-copy slice of the frame buffer.
/// </summary>
public static class MessageFramer
{
    private const int MinimumFrameWriterCapacity = 256;

    /// <summary>
    /// Header size: 4 (length) + 4 (messageId) + 1 (type) = 9 bytes
    /// </summary>
    public const int HeaderSize = 9;

    /// <summary>
    /// Length prefix written before the serialized envelope so the trailing payload can be
    /// located without the serializer reporting how many bytes it consumed.
    /// </summary>
    public const int EnvelopeLengthSize = 4;

    /// <summary>
    /// Maximum message size (16 MB).
    /// </summary>
    public const int MaxMessageSize = 16 * 1024 * 1024;

    /// <summary>
    /// A framed message read from a stream by <see cref="ReadMessageAsync(Stream, CancellationToken)"/>.
    /// <see cref="Body"/> is the message body only; the caller owns it and must dispose it.
    /// </summary>
    public readonly record struct FramedMessage(int MessageId, MessageType Type, Payload Body);

    /// <summary>
    /// Writes a complete frame (header + payload) into the supplied buffer writer.
    /// </summary>
    public static void WriteFrame(IBufferWriter<byte> writer, int messageId, MessageType type, ReadOnlySpan<byte> payload)
    {
        MessageFrameReader.ThrowIfUndefinedMessageType(type);

        var totalLength = MessageFrameReader.GetOutgoingFrameLength(payload.Length);
        var span = writer.GetSpan(totalLength);

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)type;

        if (payload.Length > 0)
        {
            payload.CopyTo(span.Slice(HeaderSize));
        }

        writer.Advance(totalLength);
    }

    /// <summary>
    /// Frames a message into an exact-size rented <see cref="Payload"/>. The caller owns the result.
    /// </summary>
    public static Payload FrameToPayload(int messageId, MessageType type, ReadOnlySpan<byte> payload)
    {
        MessageFrameReader.ThrowIfUndefinedMessageType(type);

        var totalLength = MessageFrameReader.GetOutgoingFrameLength(payload.Length);
        var result = Payload.Rent(totalLength);
        var span = result.Memory.Span;

        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), totalLength);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), messageId);
        span[8] = (byte)type;

        if (payload.Length > 0)
        {
            payload.CopyTo(span.Slice(HeaderSize));
        }

        return result;
    }

    /// <summary>
    /// Serializes <paramref name="envelope"/> and appends the raw <paramref name="payload"/> bytes
    /// behind a frame header into a single pooled buffer, then patches the total length and the
    /// envelope length. The caller owns the returned <see cref="Payload"/>.
    /// </summary>
    public static Payload FrameMessage<T>(
        ISerializer serializer,
        int messageId,
        MessageType type,
        T envelope,
        ReadOnlySpan<byte> payload)
    {
        using var writer = RentFrameWriter(payload.Length);
        WriteFramePrefix(writer, messageId, type);

        var envelopeStart = writer.WrittenCount;
        serializer.Serialize(writer, envelope);
        var envelopeLength = writer.WrittenCount - envelopeStart;

        if (payload.Length > 0)
        {
            var span = writer.GetSpan(payload.Length);
            payload.CopyTo(span);
            writer.Advance(payload.Length);
        }

        return FinishFrame(writer, envelopeLength);
    }

    internal static PooledBufferWriter RentFrameMessage<T>(
        ISerializer serializer,
        int messageId,
        MessageType type,
        T envelope,
        ReadOnlySpan<byte> payload)
    {
        var writer = RentFrameWriter(payload.Length);
        try
        {
            WriteFramePrefix(writer, messageId, type);

            var envelopeStart = writer.WrittenCount;
            serializer.Serialize(writer, envelope);
            var envelopeLength = writer.WrittenCount - envelopeStart;

            if (payload.Length > 0)
            {
                var span = writer.GetSpan(payload.Length);
                payload.CopyTo(span);
                writer.Advance(payload.Length);
            }

            CompleteFrame(writer, envelopeLength);
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Serializes <paramref name="envelope"/> followed immediately by <paramref name="argument"/>
    /// behind a frame header into a single pooled buffer. Unlike <see cref="FrameMessage{T}"/> this
    /// serializes the argument straight into the frame writer, avoiding the intermediate payload
    /// buffer and the copy. The caller owns the returned <see cref="Payload"/>.
    /// </summary>
    public static Payload FrameRequest<TEnvelope, TArgument>(
        ISerializer serializer,
        int messageId,
        MessageType type,
        TEnvelope envelope,
        TArgument argument)
    {
        using var writer = RentFrameWriter();
        WriteFramePrefix(writer, messageId, type);

        var envelopeStart = writer.WrittenCount;
        serializer.Serialize(writer, envelope);
        var envelopeLength = writer.WrittenCount - envelopeStart;

        serializer.Serialize(writer, argument);

        return FinishFrame(writer, envelopeLength);
    }

    internal static PooledBufferWriter RentFrameRequest<TEnvelope, TArgument>(
        ISerializer serializer,
        int messageId,
        MessageType type,
        TEnvelope envelope,
        TArgument argument)
    {
        var writer = RentFrameWriter();
        try
        {
            WriteFramePrefix(writer, messageId, type);

            var envelopeStart = writer.WrittenCount;
            serializer.Serialize(writer, envelope);
            var envelopeLength = writer.WrittenCount - envelopeStart;

            serializer.Serialize(writer, argument);

            CompleteFrame(writer, envelopeLength);
            return writer;
        }
        catch
        {
            writer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reserves the frame header and envelope-length prefix at the head of <paramref name="writer"/>.
    /// Callers serialize the envelope next (recording its length) and finish with
    /// <see cref="FinishFrame"/>, which patches both length fields. Lets the server frame a response
    /// envelope and have the dispatcher serialize the result straight into the same writer.
    /// </summary>
    internal static void WriteFramePrefix(PooledBufferWriter writer, int messageId, MessageType type)
    {
        MessageFrameReader.ThrowIfUndefinedMessageType(type);

        // Reserve the header + envelope-length prefix; both length fields are patched in by FinishFrame.
        var prefix = writer.GetSpan(HeaderSize + EnvelopeLengthSize);
        BinaryPrimitives.WriteInt32LittleEndian(prefix.Slice(4, 4), messageId);
        prefix[8] = (byte)type;
        writer.Advance(HeaderSize + EnvelopeLengthSize);
    }

    internal static PooledBufferWriter RentFrameWriter(int knownPayloadLength = 0)
        => PooledBufferWriter.Rent(Math.Max(
            HeaderSize + EnvelopeLengthSize + knownPayloadLength,
            MinimumFrameWriterCapacity), MaxMessageSize);

    /// <summary>
    /// Detaches the written bytes as a <see cref="Payload"/> and patches the total length and the
    /// envelope length fields reserved by <see cref="WriteFramePrefix"/>. The caller owns the result.
    /// </summary>
    internal static Payload FinishFrame(PooledBufferWriter writer, int envelopeLength)
    {
        CompleteFrame(writer, envelopeLength);
        var frame = writer.DetachPayload();
        return frame;
    }

    internal static void CompleteFrame(PooledBufferWriter writer, int envelopeLength)
    {
        var header = writer.WrittenSpan;
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(0, 4), writer.WrittenCount);
        BinaryPrimitives.WriteInt32LittleEndian(header.Slice(HeaderSize, EnvelopeLengthSize), envelopeLength);
    }

    /// <summary>
    /// Validates that <paramref name="frame"/> is a well-formed outgoing wire frame: at least a full
    /// header, a length prefix that exactly matches the buffer length, and within
    /// <paramref name="maxMessageSize"/>. Shared by every transport's send path so a malformed frame
    /// is rejected locally instead of being shipped to the peer (where behaviour would otherwise
    /// differ by transport). Throws <see cref="InvalidDataException"/> on a bad frame.
    /// </summary>
    public static void ValidateOutgoingFrame(ReadOnlySpan<byte> frame, int maxMessageSize = MaxMessageSize)
        => MessageFrameReader.ValidateOutgoingFrame(frame, maxMessageSize);

    /// <summary>
    /// Parses an un-nested RPC frame out of an in-memory buffer without copying. Both
    /// <paramref name="envelope"/> and <paramref name="payload"/> are slices of
    /// <paramref name="source"/> and share its lifetime.
    /// </summary>
    public static bool TryReadFrame(
        ReadOnlyMemory<byte> source,
        out int messageId,
        out MessageType type,
        out ReadOnlyMemory<byte> envelope,
        out ReadOnlyMemory<byte> payload)
        => MessageFrameReader.TryReadFrame(source, out messageId, out type, out envelope, out payload);

    /// <summary>
    /// Parses just the DotBoxD frame header. This supports envelope-less control frames
    /// such as request cancellation without requiring an RPC envelope prefix.
    /// </summary>
    public static bool TryReadFrameHeader(
        ReadOnlyMemory<byte> source,
        out int messageId,
        out MessageType type)
        => MessageFrameReader.TryReadFrameHeader(source, out messageId, out type);

    /// <summary>Reads a framed message using the default finite idle timeout.</summary>
    public static async Task<FramedMessage?> ReadMessageAsync(
        Stream stream,
        CancellationToken ct = default)
        => await MessageStreamFramer.ReadMessageAsync(stream, ct).ConfigureAwait(false);

    /// <summary>Reads a framed message with an explicit frame-read idle timeout.</summary>
    public static async Task<FramedMessage?> ReadMessageAsync(
        Stream stream,
        TimeSpan frameReadIdleTimeout,
        CancellationToken ct = default)
        => await MessageStreamFramer.ReadMessageAsync(stream, frameReadIdleTimeout, ct).ConfigureAwait(false);

    /// <summary>
    /// Writes a framed message to a stream.
    /// </summary>
    public static async Task WriteMessageAsync(
        Stream stream,
        int messageId,
        MessageType type,
        ReadOnlyMemory<byte> payload,
        CancellationToken ct = default)
        => await MessageStreamFramer.WriteMessageAsync(stream, messageId, type, payload, ct).ConfigureAwait(false);
}
