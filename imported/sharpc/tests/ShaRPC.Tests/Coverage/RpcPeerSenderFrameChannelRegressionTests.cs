using System.Buffers.Binary;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Transport;
using Xunit;

namespace ShaRPC.Tests.Cov.Transport;

public sealed class RpcPeerSenderFrameChannelRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SendFrameValueAsync_DisposesFrame_WhenSendLockWaitIsCanceled()
    {
        var channel = new BlockingFrameChannel();
        using var sender = new RpcPeerSender(channel, static () => false);

        var first = CreateValidFrame();
        var heldSend = sender.SendFrameValueAsync(first, CancellationToken.None).AsTask();
        await channel.SendEntered.Task.WaitAsync(Timeout);

        var second = CreateValidFrame();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendFrameValueAsync(second, cts.Token).AsTask());

        AssertDisposed(second);

        channel.Release();
        await heldSend.WaitAsync(Timeout);
    }

    [Fact]
    public async Task SendFrameValueAsync_ValidatesFrame_BeforeFrameChannelTransfer()
    {
        var channel = new BlockingFrameChannel();
        using var sender = new RpcPeerSender(channel, static () => false);
        var frame = CreateLengthMismatchFrame();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => sender.SendFrameValueAsync(frame, CancellationToken.None).AsTask());

        Assert.Equal(0, channel.SendFrameCalls);
        AssertDisposed(frame);
    }

    private static PooledBufferWriter CreateValidFrame()
    {
        var frame = new PooledBufferWriter();
        MessageFramer.WriteFrame(frame, 1, MessageType.Request, ReadOnlySpan<byte>.Empty);
        return frame;
    }

    private static PooledBufferWriter CreateLengthMismatchFrame()
    {
        var frame = new PooledBufferWriter();
        var span = frame.GetSpan(MessageFramer.HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(0, 4), MessageFramer.MaxMessageSize + 1);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), 1);
        span[8] = (byte)MessageType.Request;
        frame.Advance(MessageFramer.HeaderSize);
        return frame;
    }

    private static void AssertDisposed(PooledBufferWriter frame) =>
        Assert.Throws<ObjectDisposedException>(() => _ = frame.WrittenMemory);

    private sealed class BlockingFrameChannel : IRpcFrameChannel
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected => true;

        public string RemoteEndpoint => "frame://test";

        public int SendFrameCalls { get; private set; }

        public TaskCompletionSource SendEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            SendValueAsync(data, ct).AsTask();

        public ValueTask SendValueAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            default;

        public async ValueTask SendFrameValueAsync(PooledBufferWriter frame, CancellationToken ct = default)
        {
            SendFrameCalls++;
            SendEntered.TrySetResult();
            try
            {
                await _release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                frame.Dispose();
            }
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default) =>
            ReceiveValueAsync(ct).AsTask();

        public ValueTask<Payload> ReceiveValueAsync(CancellationToken ct = default) =>
            new(Payload.Empty);

        public ValueTask<RpcFrame> ReceiveFrameValueAsync(CancellationToken ct = default) =>
            new(new RpcFrame(Payload.Empty));

        public ValueTask DisposeAsync()
        {
            Release();
            return default;
        }

        public void Release() => _release.TrySetResult();
    }
}
