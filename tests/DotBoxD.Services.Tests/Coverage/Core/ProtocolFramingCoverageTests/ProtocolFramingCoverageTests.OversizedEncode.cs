using System.Buffers.Binary;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class MessageFramerCoverageTests
{
    [Fact]
    public void FrameToPayload_WhenTotalLengthExceedsMaxMessageSize_ThrowsBeforeCreatingFrame()
    {
        var payload = new byte[OversizedPayloadLength];

        var ex = Assert.Throws<InvalidDataException>(
            () =>
            {
                using var _ = MessageFramer.FrameToPayload(1, MessageType.Request, payload);
            });

        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
        Assert.Contains(OversizedFrameLength.ToString(), ex.Message);
    }

    [Fact]
    public void WriteFrame_WhenTotalLengthExceedsMaxMessageSize_ThrowsBeforeWritingFrame()
    {
        var payload = new byte[OversizedPayloadLength];
        using var writer = new PooledBufferWriter();

        var ex = Assert.Throws<InvalidDataException>(
            () => MessageFramer.WriteFrame(writer, 1, MessageType.Request, payload));

        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
        Assert.Contains(OversizedFrameLength.ToString(), ex.Message);
        Assert.Equal(0, writer.WrittenCount);
    }

    [Fact]
    public async Task WriteMessageAsync_WhenTotalLengthExceedsMaxMessageSize_ThrowsBeforeWritingFrame()
    {
        var payload = new byte[OversizedPayloadLength];
        using var stream = new CountingWriteStream();

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => MessageFramer.WriteMessageAsync(stream, 1, MessageType.Request, payload)
                .AsTaskWithTimeout(Timeout));

        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
        Assert.Contains(OversizedFrameLength.ToString(), ex.Message);
        Assert.Equal(0, stream.BytesWritten);
    }

    [Fact]
    public void ValidateOutgoingFrame_WhenDeclaredLengthIsOneByteAboveMaxMessageSize_Throws()
    {
        var frame = new byte[OversizedFrameLength];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length);
        frame[8] = (byte)MessageType.Request;

        var ex = Assert.Throws<InvalidDataException>(() => MessageFramer.ValidateOutgoingFrame(frame));

        Assert.Contains("Invalid DotBoxD frame length", ex.Message);
        Assert.Contains(OversizedFrameLength.ToString(), ex.Message);
    }

    private const int OversizedFrameLength = MessageFramer.MaxMessageSize + 1;
    private const int OversizedPayloadLength = OversizedFrameLength - MessageFramer.HeaderSize;

    private sealed class CountingWriteStream : Stream
    {
        public long BytesWritten { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            BytesWritten += buffer.Length;
            return ValueTask.CompletedTask;
        }
    }
}
