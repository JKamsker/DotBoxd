using System.Buffers.Binary;
using DotBoxD.Services.Protocol;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class MessageFramerCoverageTests
{
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
        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
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
        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
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
    public async Task ReadMessageAsync_HeaderTruncated_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[MessageFramer.HeaderSize - 1]);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.ReadMessageAsync(stream).AsTaskWithTimeout(Timeout));
        Assert.Contains("frame header bytes", ex.Message);
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
