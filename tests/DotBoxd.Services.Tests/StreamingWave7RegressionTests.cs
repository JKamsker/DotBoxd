using System.Buffers;
using System.IO.Pipelines;
using DotBoxd.Services;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

public sealed class StreamingWave7RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PipeOutbound_WhenItemSendFails_AdvancesReadBuffer()
    {
        var serializer = new MessagePackRpcSerializer();
        var itemSendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 0));
        var streams = new RpcStreamManager(
            serializer,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                if (type == MessageType.StreamItem)
                {
                    itemSendStarted.TrySetResult();
                    throw new InvalidOperationException("Stream item send failed.");
                }

                return Task.CompletedTask;
            },
            exceptionTransformer: null);
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        await using var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromPipe(handle, pipe) },
            CancellationToken.None);
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));

        outbound.Start();
        pipe.Writer.Write(new byte[] { 1 });
        var flush = pipe.Writer.FlushAsync().AsTask();

        await itemSendStarted.Task.WaitAsync(TestTimeout);
        await flush.WaitAsync(TestTimeout);
        await outbound.WaitAsync().WaitAsync(TestTimeout);

        Assert.Equal(0, streams.OutboundSenderCount);
        await pipe.Reader.CompleteAsync();
        await pipe.Writer.CompleteAsync();
    }

    [Fact]
    public async Task StartedOutboundSet_DisposeDisposesOwnedSourceWhenReadIgnoresCancellation()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var source = new CancellationIgnoringStream();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromStream(handle, source, leaveOpen: false) },
            CancellationToken.None);
        outbound.Start();
        await source.ReadStarted.WaitAsync(TestTimeout);

        await outbound.DisposeAsync().AsTask().WaitAsync(TestTimeout);

        Assert.True(source.Disposed);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    [Fact]
    public async Task WaitAsync_WithMultipleStreams_WaitsAfterOnePumpCompletes()
    {
        var serializer = new MessagePackRpcSerializer();
        var firstCompleteSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RpcStreamHandle firstHandle = default;
        var streams = new RpcStreamManager(
            serializer,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out var streamId, out var type));
                if (streamId == firstHandle.StreamId && type == MessageType.StreamComplete)
                {
                    firstCompleteSent.TrySetResult();
                }

                return Task.CompletedTask;
            },
            exceptionTransformer: null);
        firstHandle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var secondHandle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var blockingSource = new CancellationIgnoringStream();
        await using var outbound = streams.RegisterOutbound(
            new[]
            {
                RpcStreamAttachment.FromStream(firstHandle, new MemoryStream(), leaveOpen: false),
                RpcStreamAttachment.FromStream(secondHandle, blockingSource, leaveOpen: false),
            },
            CancellationToken.None);

        outbound.Start();
        await firstCompleteSent.Task.WaitAsync(TestTimeout);
        await blockingSource.ReadStarted.WaitAsync(TestTimeout);
        await WaitUntilAsync(() => streams.OutboundSenderCount == 1);

        var wait = outbound.WaitAsync();

        Assert.NotSame(wait, await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(100))));
        blockingSource.ReleaseRead();
        await wait.WaitAsync(TestTimeout);
        Assert.Equal(0, streams.OutboundSenderCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TestTimeout);
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cts.Token).ConfigureAwait(false);
        }
    }

    private sealed class CancellationIgnoringStream : Stream
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _readReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public bool Disposed { get; private set; }

        public void ReleaseRead() =>
            _readReleased.TrySetResult(0);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult();
            return new ValueTask<int>(_readReleased.Task);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _readReleased.TrySetResult(0);
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            _readReleased.TrySetResult(0);
            return default;
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
