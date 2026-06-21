using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport;

public sealed class StreamConnectionSendDisposeRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task DisposeAsync_CompletesSendWaitingForSendLock()
    {
        await using var stream = new BlockingWriteStream();
        await using var connection = new StreamConnection(stream, ownsStream: false);
        using var frame = MessageFramer.FrameToPayload(1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        using var secondFrame = MessageFramer.FrameToPayload(2, MessageType.Request, ReadOnlySpan<byte>.Empty);

        var firstSend = connection.SendAsync(frame.Memory);
        await stream.WriteEntered.WaitAsync(Timeout);

        var secondSend = connection.SendAsync(secondFrame.Memory);
        await connection.DisposeAsync();

        try
        {
            var exception = await Record.ExceptionAsync(() => secondSend.WaitAsync(Timeout));

            Assert.NotNull(exception);
            Assert.IsNotType<TimeoutException>(exception);
        }
        finally
        {
            stream.CompleteWrites();
            await Record.ExceptionAsync(() => firstSend.WaitAsync(Timeout));
            await Record.ExceptionAsync(() => secondSend.WaitAsync(Timeout));
        }
    }

    private sealed class BlockingWriteStream : Stream
    {
        private readonly TaskCompletionSource _writeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _writesReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WriteEntered => _writeEntered.Task;

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void CompleteWrites() => _writesReleased.TrySetResult();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _writeEntered.TrySetResult();
            return new ValueTask(_writesReleased.Task.WaitAsync(cancellationToken));
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
