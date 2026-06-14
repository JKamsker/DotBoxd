using DotBoxd.Services;
using DotBoxd.Services.Exceptions;
using DotBoxd.Services.Protocol;
using DotBoxd.Services.Streaming;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

public sealed class RpcStreamManagerRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RemoveInbound_AbortsAndDrainsQueuedChunks()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(91, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        streams.RemoveInbound(handle.StreamId);

        await Assert.ThrowsAsync<DotBoxdRpcConnectionException>(() =>
            receiver.ReadChunkAsync(CancellationToken.None).AsTask().WaitAsync(Timeout));
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task RemoveInbound_UnblocksPipeBridgeReader()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(92, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        var pipe = RpcPipeBridge.CreateReadablePipe(receiver, CancellationToken.None);

        streams.RemoveInbound(handle.StreamId);

        await Assert.ThrowsAsync<DotBoxdRpcConnectionException>(() =>
            pipe.Reader.ReadAsync().AsTask().WaitAsync(Timeout));
        await pipe.Reader.CompleteAsync();
    }

    [Fact]
    public async Task ActiveStreamOverCredit_DoesNotThrowOutOfFrameProcessing()
    {
        var streams = CreateStreamManager();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var attachment = RpcStreamAttachment.FromStream(
            handle,
            new MemoryStream(new byte[] { 1 }));
        await using var outbound = streams.RegisterOutbound(new[] { attachment }, CancellationToken.None);
        using var maxCredit = RpcRawFrame.FrameInt32(
            handle.StreamId,
            MessageType.StreamCredit,
            int.MaxValue);
        using var extraCredit = RpcRawFrame.FrameInt32(
            handle.StreamId,
            MessageType.StreamCredit,
            1);

        Assert.True(streams.TryAddCredit(maxCredit));
        var accepted = false;
        var error = Record.Exception(() => accepted = streams.TryAddCredit(extraCredit));

        Assert.Null(error);
        Assert.True(accepted);
    }

    [Fact]
    public void PendingCreditAddedAfterReservationRelease_IsPruned()
    {
        var streams = CreateStreamManager();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        streams.AfterReservedOutboundCreditObservedForTest = streams.ReleaseOutboundReservation;
        using var credit = RpcRawFrame.FrameInt32(
            handle.StreamId,
            MessageType.StreamCredit,
            1);

        Assert.True(streams.TryAddCredit(credit));

        Assert.Equal(0, streams.PendingCreditCount);
    }

    [Fact]
    public async Task TryAddCredit_WhenRegistrationCompletesAfterSenderMiss_CreditsActiveSender()
    {
        var sentItem = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streams = CreateStreamManager((frame, ct) =>
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
            if (type == MessageType.StreamItem)
            {
                sentItem.TrySetResult();
            }

            return Task.CompletedTask;
        });
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        RpcOutboundStreamSet? outbound = null;
        streams.AfterOutboundSenderMissForTest = streamId =>
        {
            if (streamId == handle.StreamId)
            {
                outbound = streams.RegisterOutbound(
                    new[] { RpcStreamAttachment.FromStream(handle, new MemoryStream()) },
                    CancellationToken.None);
            }
        };
        using var credit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        using var sendCts = new CancellationTokenSource();

        try
        {
            Assert.True(streams.TryAddCredit(credit));
            await streams.SendStreamItemAsync(handle.StreamId, new byte[] { 1 }, sendCts.Token)
                .WaitAsync(Timeout);
            await sentItem.Task.WaitAsync(Timeout);
        }
        finally
        {
            sendCts.Cancel();
            if (outbound is not null)
            {
                await outbound.DisposeAsync().AsTask().WaitAsync(Timeout);
            }
        }
    }

    [Fact]
    public async Task CancelOutbound_WhenRegistrationCompletesAfterSenderMiss_CancelsActiveSender()
    {
        var streams = CreateStreamManager();
        var handle = streams.ReserveOutbound(RpcStreamKind.Binary);
        var source = new CancellationObservingStream();
        RpcOutboundStreamSet? outbound = null;
        streams.AfterOutboundSenderMissForTest = streamId =>
        {
            if (streamId == handle.StreamId)
            {
                outbound = streams.RegisterOutbound(
                    new[] { RpcStreamAttachment.FromStream(handle, source, leaveOpen: false) },
                    CancellationToken.None);
            }
        };

        try
        {
            streams.CancelOutbound(handle.StreamId);
            Assert.NotNull(outbound);
            outbound!.Start();

            await source.Canceled.WaitAsync(Timeout);
        }
        finally
        {
            if (outbound is not null)
            {
                await outbound.DisposeAsync().AsTask().WaitAsync(Timeout);
            }
        }
    }

    private static RpcStreamManager CreateStreamManager()
        => CreateStreamManager(SendNoopAsync);

    private static RpcStreamManager CreateStreamManager(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync)
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, sendAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private sealed class CancellationObservingStream : Stream
    {
        private readonly TaskCompletionSource _canceled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> _readReleased =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Canceled => _canceled.Task;

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
            if (cancellationToken.IsCancellationRequested)
            {
                _canceled.TrySetResult();
                return ValueTask.FromCanceled<int>(cancellationToken);
            }

            return new ValueTask<int>(_readReleased.Task);
        }

        protected override void Dispose(bool disposing)
        {
            _readReleased.TrySetResult(0);
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
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
