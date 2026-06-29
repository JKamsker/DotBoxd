using System.Buffers.Binary;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Behavioral coverage for the wire codec (<see cref="MessageFramer"/>) — every public method and
/// every <see cref="MessageType"/> branch — driven through real framing/parsing scenarios:
/// round-trips, header-field boundaries, malformed/short/oversized frames, zero-length and large
/// payloads, error-response framing with <see cref="RpcErrorInfo"/>, and the
/// stream read/write path including truncation and mid-read failures.
/// </summary>
public sealed partial class MessageFramerCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // ---- WriteFrame / FrameToPayload: header field boundaries -------------------------------

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Error)]
    [InlineData(MessageType.Cancel)]
    public void WriteFrame_AnyMessageType_EncodesHeaderFieldsAndPayload(MessageType type)
    {
        // Arrange
        var messageId = unchecked((int)0xDEADBEEF);
        var payload = new byte[] { 7, 8, 9 };
        using var writer = new PooledBufferWriter();

        // Act
        MessageFramer.WriteFrame(writer, messageId, type, payload);
        var span = writer.WrittenMemory.Span;

        // Assert
        Assert.Equal(MessageFramer.HeaderSize + payload.Length, writer.WrittenCount);
        Assert.Equal(writer.WrittenCount, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4)));
        Assert.Equal(messageId, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));
        Assert.Equal((byte)type, span[8]);
        Assert.Equal(payload, span.Slice(MessageFramer.HeaderSize).ToArray());
    }

    [Fact]
    public void FrameToPayload_WithZeroLengthPayload_ProducesHeaderOnlyFrame()
    {
        // Act
        using var frame = MessageFramer.FrameToPayload(int.MaxValue, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        var span = frame.Span;

        // Assert
        Assert.Equal(MessageFramer.HeaderSize, frame.Length);
        Assert.Equal(MessageFramer.HeaderSize, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4)));
        Assert.Equal(int.MaxValue, BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)));
        Assert.Equal((byte)MessageType.Cancel, span[8]);
    }

    [Fact]
    public void FrameToPayload_WithNegativeMessageId_RoundTripsViaHeaderReader()
    {
        // Arrange
        var messageId = int.MinValue;

        // Act
        using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Response, new byte[] { 1 });
        var ok = MessageFramer.TryReadFrameHeader(frame.Memory, out var readId, out var readType);

        // Assert
        Assert.True(ok);
        Assert.Equal(messageId, readId);
        Assert.Equal(MessageType.Response, readType);
    }

    [Fact]
    public void FrameWriters_UndefinedMessageType_Throws()
    {
        var invalidType = (MessageType)0x7F;
        using var writer = new PooledBufferWriter();

        var writeEx = Assert.Throws<ArgumentOutOfRangeException>(
            () => MessageFramer.WriteFrame(writer, 1, invalidType, ReadOnlySpan<byte>.Empty));
        var frameEx = Assert.Throws<ArgumentOutOfRangeException>(
            () => MessageFramer.FrameToPayload(1, invalidType, ReadOnlySpan<byte>.Empty));

        Assert.Equal("type", writeEx.ParamName);
        Assert.Equal("type", frameEx.ParamName);
    }

    // ---- ValidateOutgoingFrame: malformed / oversized frames (lines 173-174, 185-186) -------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(MessageFramer.HeaderSize - 1)]
    public void ValidateOutgoingFrame_FrameSmallerThanHeader_Throws(int length)
    {
        // Arrange
        var frame = new byte[length];

        // Act + Assert
        var ex = Assert.Throws<InvalidDataException>(() => MessageFramer.ValidateOutgoingFrame(frame));
        Assert.Contains("too small", ex.Message);
    }

    [Fact]
    public void ValidateOutgoingFrame_LengthPrefixMismatch_Throws()
    {
        // Arrange: a full-size buffer whose declared length disagrees with the actual buffer length.
        var frame = new byte[MessageFramer.HeaderSize + 4];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length + 100);

        // Act + Assert
        var ex = Assert.Throws<InvalidDataException>(() => MessageFramer.ValidateOutgoingFrame(frame));
        Assert.Contains("does not match buffer length", ex.Message);
    }

    [Fact]
    public void ValidateOutgoingFrame_DeclaredLengthExceedsMax_Throws()
    {
        // Arrange: a self-consistent frame (prefix == buffer length) that still exceeds the cap.
        var frame = new byte[MessageFramer.HeaderSize + 16];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length);

        // Act + Assert: pass a tiny max so the consistent-but-oversized branch is taken.
        var ex = Assert.Throws<InvalidDataException>(
            () => MessageFramer.ValidateOutgoingFrame(frame, maxMessageSize: MessageFramer.HeaderSize));
        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
        Assert.Contains(frame.Length.ToString(), ex.Message);
    }

    [Fact]
    public void ValidateOutgoingFrame_WellFormedFrame_DoesNotThrow()
    {
        // Arrange
        using var frame = MessageFramer.FrameToPayload(1, MessageType.Request, new byte[] { 1, 2, 3 });

        // Act + Assert: a real codec output must pass validation unchanged.
        MessageFramer.ValidateOutgoingFrame(frame.Span);
    }

    [Fact]
    public void ValidateOutgoingFrame_UndefinedMessageType_Throws()
    {
        using var valid = MessageFramer.FrameToPayload(1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        var frame = valid.Memory.ToArray();
        frame[8] = 0x7F;

        var ex = Assert.Throws<InvalidDataException>(() => MessageFramer.ValidateOutgoingFrame(frame));

        Assert.Contains("message type", ex.Message);
    }

    // ---- TryReadFrame: round trips + malformed (lines 208-209, 228-229) ---------------------

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Error)]
    public void TryReadFrame_EveryRpcMessageType_RoundTripsEnvelopeAndPayload(MessageType type)
    {
        // Arrange
        var serializer = NewSerializer();
        var response = new RpcResponse { MessageId = 99, IsSuccess = type == MessageType.Response };
        var payload = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        using var frame = MessageFramer.FrameMessage(serializer, 99, type, response, payload);

        // Act
        var ok = MessageFramer.TryReadFrame(frame.Memory, out var id, out var readType, out var envelope, out var readPayload);

        // Assert
        Assert.True(ok);
        Assert.Equal(99, id);
        Assert.Equal(type, readType);
        Assert.Equal(payload, readPayload.ToArray());
        var roundTripped = serializer.Deserialize<RpcResponse>(envelope);
        Assert.Equal(response.MessageId, roundTripped.MessageId);
        Assert.Equal(response.IsSuccess, roundTripped.IsSuccess);
    }

    [Fact]
    public void TryReadFrame_ErrorResponseWithRpcErrorInfo_RoundTrips()
    {
        // Arrange: model the server's error-response framing — an RpcResponse envelope carrying
        // the error message/type derived from an RpcErrorInfo, framed as MessageType.Error.
        var serializer = NewSerializer();
        var info = RpcErrorInfo.FromException(new InvalidOperationException("boom detail"));
        var response = new RpcResponse
        {
            MessageId = 17,
            IsSuccess = false,
            ErrorMessage = info.Message,
            ErrorType = info.Type,
        };
        using var frame = MessageFramer.FrameMessage(serializer, 17, MessageType.Error, response, ReadOnlySpan<byte>.Empty);

        // Act
        var ok = MessageFramer.TryReadFrame(frame.Memory, out var id, out var type, out var envelope, out var payload);

        // Assert
        Assert.True(ok);
        Assert.Equal(17, id);
        Assert.Equal(MessageType.Error, type);
        Assert.True(payload.IsEmpty);
        var roundTripped = serializer.Deserialize<RpcResponse>(envelope);
        Assert.False(roundTripped.IsSuccess);
        Assert.Equal("boom detail", roundTripped.ErrorMessage);
        Assert.Equal(nameof(InvalidOperationException), roundTripped.ErrorType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(MessageFramer.HeaderSize)]
    [InlineData(MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize - 1)]
    public void TryReadFrame_BufferTooShortForHeaderAndEnvelopePrefix_ReturnsFalse(int length)
    {
        // Arrange: anything shorter than header + envelope-length prefix cannot be an RPC frame.
        var buffer = new byte[length];

        // Act
        var ok = MessageFramer.TryReadFrame(buffer, out var id, out var type, out var envelope, out var payload);

        // Assert
        Assert.False(ok);
        Assert.Equal(0, id);
        Assert.Equal(default, type);
        Assert.True(envelope.IsEmpty);
        Assert.True(payload.IsEmpty);
    }

    [Fact]
    public void TryReadFrame_DeclaredLengthShorterThanMinimum_ReturnsFalse()
    {
        // Arrange: enough bytes present, but the length prefix declares a sub-minimal frame.
        var buffer = new byte[MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), MessageFramer.HeaderSize); // < header+prefix

        // Act + Assert
        Assert.False(MessageFramer.TryReadFrame(buffer, out _, out _, out _, out _));
    }

    [Fact]
    public void TryReadFrame_EnvelopeLengthExceedsFrame_ReturnsFalse()
    {
        // Arrange: a self-consistent total length, but the envelope-length field claims more bytes
        // than the frame holds (line 228-229: envelopeStart + envelopeLength > totalLength).
        var buffer = new byte[MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize + 2];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), buffer.Length);
        buffer[8] = (byte)MessageType.Request;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(MessageFramer.HeaderSize, 4), 1000);

        // Act + Assert
        Assert.False(MessageFramer.TryReadFrame(buffer, out _, out _, out _, out _));
    }

    [Fact]
    public void TryReadFrame_NegativeEnvelopeLength_ReturnsFalse()
    {
        // Arrange: a negative envelope length must be rejected, not used as a slice length.
        var buffer = new byte[MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize + 4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), buffer.Length);
        buffer[8] = (byte)MessageType.Request;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(MessageFramer.HeaderSize, 4), -1);

        // Act + Assert
        Assert.False(MessageFramer.TryReadFrame(buffer, out _, out _, out _, out _));
    }

    [Fact]
    public void TryReadFrame_ZeroEnvelopeAndZeroPayload_ReturnsTrueWithEmptySlices()
    {
        // Arrange: minimal valid RPC frame — empty envelope, empty payload.
        var buffer = new byte[MessageFramer.HeaderSize + MessageFramer.EnvelopeLengthSize];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), buffer.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 555);
        buffer[8] = (byte)MessageType.Cancel;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(MessageFramer.HeaderSize, 4), 0);

        // Act
        var ok = MessageFramer.TryReadFrame(buffer, out var id, out var type, out var envelope, out var payload);

        // Assert
        Assert.True(ok);
        Assert.Equal(555, id);
        Assert.Equal(MessageType.Cancel, type);
        Assert.True(envelope.IsEmpty);
        Assert.True(payload.IsEmpty);
    }

    // ---- TryReadFrameHeader: short buffer + every type (lines 251-252) ----------------------

}
