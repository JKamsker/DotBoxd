using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Serialization;
using DotBoxd.Codecs.MessagePack;
using DotBoxd.Services.Tests;
using Xunit;

namespace DotBoxd.Services.Tests.Cov.Protocol;

/// <summary>
/// Behavioral coverage for the wire codec (<see cref="MessageFramer"/>) — every public method and
/// every <see cref="MessageType"/> branch — driven through real framing/parsing scenarios:
/// round-trips, header-field boundaries, malformed/short/oversized frames, zero-length and large
/// payloads, error-response framing with <see cref="DotBoxd.Services.RpcErrorInfo"/>, and the
/// stream read/write path including truncation and mid-read failures.
/// </summary>
public sealed class MessageFramerCoverageTests
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
        Assert.Contains("Invalid DotBoxd frame length", ex.Message);
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
        var info = DotBoxd.Services.RpcErrorInfo.FromException(new InvalidOperationException("boom detail"));
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(MessageFramer.HeaderSize - 1)]
    public void TryReadFrameHeader_BufferShorterThanHeader_ReturnsFalse(int length)
    {
        // Arrange
        var buffer = new byte[length];

        // Act
        var ok = MessageFramer.TryReadFrameHeader(buffer, out var id, out var type);

        // Assert
        Assert.False(ok);
        Assert.Equal(0, id);
        Assert.Equal(default, type);
    }

    [Fact]
    public void TryReadFrameHeader_DeclaredLengthShorterThanHeader_ReturnsFalse()
    {
        // Arrange: buffer is header-sized but the length prefix declares fewer bytes than a header.
        var buffer = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(0, 4), MessageFramer.HeaderSize - 1);

        // Act + Assert
        Assert.False(MessageFramer.TryReadFrameHeader(buffer, out _, out _));
    }

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Error)]
    [InlineData(MessageType.Cancel)]
    public void TryReadFrameHeader_EnvelopeLessControlFrame_ReadsIdAndType(MessageType type)
    {
        // Arrange: a header-only frame (the cancel/control shape) must parse with just the header.
        using var frame = MessageFramer.FrameToPayload(0x01020304, type, ReadOnlySpan<byte>.Empty);

        // Act
        var ok = MessageFramer.TryReadFrameHeader(frame.Memory, out var id, out var readType);

        // Assert
        Assert.True(ok);
        Assert.Equal(0x01020304, id);
        Assert.Equal(type, readType);
    }

    // ---- ReadMessageAsync: stream read path, truncation, mid-read failure -------------------

    [Fact]
    public async Task ReadMessageAsync_DeclaredLengthBelowHeader_ThrowsInvalidData()
    {
        // Arrange: a header whose length field is smaller than HeaderSize (line 288-289).
        var header = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), MessageFramer.HeaderSize - 1);
        using var stream = new MemoryStream(header);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout));
        Assert.Contains("Invalid DotBoxd frame length", ex.Message);
    }

    [Fact]
    public async Task ReadMessageAsync_DeclaredLengthAboveMax_ThrowsInvalidData()
    {
        // Arrange: a header whose length field exceeds MaxMessageSize (line 288-289).
        var header = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), MessageFramer.MaxMessageSize + 1);
        using var stream = new MemoryStream(header);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout));
        Assert.Contains("Invalid DotBoxd frame length", ex.Message);
    }

    [Fact]
    public async Task ReadMessageAsync_PayloadTruncatedMidFrame_ThrowsInvalidDataException()
    {
        // Arrange: a valid header announcing a payload, but the stream ends partway through it.
        // A truncated frame is a protocol error, not a graceful disconnect.
        var messageId = 5;
        var fullPayload = new byte[64];
        new Random(7).NextBytes(fullPayload);
        using var frame = MessageFramer.FrameToPayload(messageId, MessageType.Request, fullPayload);

        // Keep the full header but drop the last 10 payload bytes so the read can never complete.
        var truncated = frame.Memory.Slice(0, frame.Length - 10).ToArray();
        using var stream = new MemoryStream(truncated);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout));
    }

    [Fact]
    public async Task ReadMessageAsync_HeaderTruncated_ReturnsNull()
    {
        // Arrange: fewer than HeaderSize bytes -> connection-closed signal (line 281-284).
        using var stream = new MemoryStream(new byte[MessageFramer.HeaderSize - 1]);

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ReadMessageAsync_StreamThrowsDuringPayloadRead_DisposesPayloadAndRethrows()
    {
        // Arrange: header reads fine, then the payload read throws. The framer must dispose the
        // rented payload and rethrow (lines 309-312). Returning the buffer to the pool on the
        // failure path is the behavior under test; we assert the exception propagates unchanged.
        var sentinel = new IOException("payload read failed");
        var header = new byte[MessageFramer.HeaderSize];
        var payloadLength = 32;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), MessageFramer.HeaderSize + payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), 1);
        header[8] = (byte)MessageType.Request;

        using var stream = new ScriptedReadStream(header, sentinel);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<IOException>(
            () => MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout));
        Assert.Same(sentinel, ex);
    }

    [Fact]
    public async Task ReadMessageAsync_ZeroLengthPayloadFrame_ReturnsEmptyBody()
    {
        // Arrange: header-only frame -> FramedMessage.Body is the shared Empty payload.
        using var frame = MessageFramer.FrameToPayload(8, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        using var stream = new MemoryStream(frame.Memory.ToArray());

        // Act
        var result = await MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout);

        // Assert
        Assert.NotNull(result);
        var msg = result!.Value;
        try
        {
            Assert.Equal(8, msg.MessageId);
            Assert.Equal(MessageType.Cancel, msg.Type);
            Assert.Equal(0, msg.Body.Length);
        }
        finally
        {
            msg.Body.Dispose();
        }
    }

    // ---- WriteMessageAsync round trip across all types via the stream path ------------------

    [Theory]
    [InlineData(MessageType.Request)]
    [InlineData(MessageType.Response)]
    [InlineData(MessageType.Error)]
    [InlineData(MessageType.Cancel)]
    public async Task WriteThenReadMessageAsync_PreservesIdTypeAndBody(MessageType type)
    {
        // Arrange
        var messageId = 271828;
        var payload = new byte[] { 3, 1, 4, 1, 5, 9 };
        using var stream = new MemoryStream();

        // Act
        await MessageFramer.WriteMessageAsync(stream, messageId, type, payload).AsTaskWithTimeout(Timeout);
        stream.Position = 0;
        var result = await MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout);

        // Assert
        Assert.NotNull(result);
        var msg = result!.Value;
        try
        {
            Assert.Equal(messageId, msg.MessageId);
            Assert.Equal(type, msg.Type);
            Assert.Equal(payload, msg.Body.Memory.ToArray());
        }
        finally
        {
            msg.Body.Dispose();
        }
    }

    /// <summary>
    /// A stream that returns a fixed header in full, then throws on the next read — used to drive
    /// <see cref="MessageFramer.ReadMessageAsync"/> down its payload-read failure path.
    /// </summary>
    private sealed class ScriptedReadStream : Stream
    {
        private readonly byte[] _header;
        private readonly Exception _failOnPayload;
        private int _headerOffset;

        public ScriptedReadStream(byte[] header, Exception failOnPayload)
        {
            _header = header;
            _failOnPayload = failOnPayload;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_headerOffset < _header.Length)
            {
                var toCopy = Math.Min(buffer.Length, _header.Length - _headerOffset);
                _header.AsSpan(_headerOffset, toCopy).CopyTo(buffer.Span);
                _headerOffset += toCopy;
                return ValueTask.FromResult(toCopy);
            }

            // Header fully delivered; the payload read fails.
            throw _failOnPayload;
        }
    }
}

/// <summary>
/// Helpers so every potentially-blocking await in this file carries a hard timeout: a regression
/// fails fast instead of hanging CI.
/// </summary>
internal static class FramingTaskExtensions
{
    public static Task<T> AsTaskWithTimeout<T>(this Task<T> task, TimeSpan timeout) =>
        task.WaitAsync(timeout);

    public static Task<T> AsTaskWithTimeout<T>(this ValueTask<T> task, TimeSpan timeout) =>
        task.AsTask().WaitAsync(timeout);

    public static Task AsTaskWithTimeout(this Task task, TimeSpan timeout) =>
        task.WaitAsync(timeout);
}
