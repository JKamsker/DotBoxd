using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingWave9RegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task OverWindowStreamItem_DisposesRejectedFrameWithoutSendingCredit()
    {
        var credits = new ConcurrentQueue<int>();
        var streams = CreateStreamManager((frame, ct) =>
        {
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) &&
                type == MessageType.StreamCredit &&
                RpcRawFrame.TryReadInt32(frame, out var count))
            {
                credits.Enqueue(count);
            }

            return Task.CompletedTask;
        });
        var handle = new RpcStreamHandle(901, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        await WaitUntilAsync(() => credits.Count == 1);

        for (var i = 0; i < RpcStreamManager.WindowSize; i++)
        {
            var accepted = MessageFramer.FrameToPayload(
                handle.StreamId,
                MessageType.StreamItem,
                new byte[] { (byte)i });
            Assert.True(streams.TryAcceptItem(handle.StreamId, accepted));
        }

        var rejected = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 99 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, rejected));

        Assert.Throws<ObjectDisposedException>(() => _ = rejected.Memory);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            receiver.ReadChunkAsync(CancellationToken.None).AsTask().WaitAsync(TestTimeout));
        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(new[] { RpcStreamManager.WindowSize }, credits.ToArray());
    }

    [Fact]
    public async Task ReadablePipe_ReaderCompletionWhileRemoteIdle_SendsStreamCancel()
    {
        var cancelSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var streams = CreateStreamManager((frame, ct) =>
        {
            if (MessageFramer.TryReadFrameHeader(frame, out _, out var type) &&
                type == MessageType.StreamCancel)
            {
                cancelSent.TrySetResult();
            }

            return Task.CompletedTask;
        });
        var handle = new RpcStreamHandle(902, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        var pipe = RpcPipeBridge.CreateReadablePipe(receiver, CancellationToken.None);
        var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        var result = await pipe.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Buffer.ToArray());
        pipe.Reader.AdvanceTo(result.Buffer.End);

        await pipe.Reader.CompleteAsync().AsTask().WaitAsync(TestTimeout);

        await cancelSent.Task.WaitAsync(TestTimeout);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task LateStreamItemAfterLocalCancel_IsConsumedWithoutProtocolError()
    {
        var serializer = new MessagePackRpcSerializer();
        var protocolErrors = new ConcurrentQueue<string>();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions(),
            streams,
            SendNoopAsync,
            (id, type, message, _) => protocolErrors.Enqueue($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });
        var outbound = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        var processor = new RpcPeerFrameProcessor(
            inbound,
            outbound,
            streams,
            (id, type, message, _) => protocolErrors.Enqueue($"{id}:{type}:{message}"));
        var handle = new RpcStreamHandle(903, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        await receiver.CancelAsync();

        var lateItem = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 4, 5, 6 });
        var shouldDispose = await processor.ShouldDisposeAsync(lateItem, CancellationToken.None);

        Assert.False(shouldDispose);
        Assert.Empty(protocolErrors);
        Assert.Throws<ObjectDisposedException>(() => _ = lateItem.Memory);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    private static RpcStreamManager CreateStreamManager(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync)
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, sendAsync, exceptionTransformer: null);
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
}
