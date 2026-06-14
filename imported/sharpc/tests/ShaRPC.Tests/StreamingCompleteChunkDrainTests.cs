using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingCompleteChunkDrainTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CompleteInbound_WithBufferedChunks_PreservesQueuedChunksUntilRead()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(42_001, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        var frame1 = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 1 });
        var frame2 = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 2 });
        var frame3 = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 3 });

        Assert.True(streams.TryAcceptItem(handle.StreamId, frame1));
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame2));
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame3));

        streams.CompleteInbound(handle.StreamId);

        Assert.Equal(0, streams.InboundReceiverCount);

        using (var chunk = await ReadRequiredChunkAsync(receiver))
        {
            Assert.Equal(new byte[] { 1 }, chunk.Payload.ToArray());
        }

        using (var chunk = await ReadRequiredChunkAsync(receiver))
        {
            Assert.Equal(new byte[] { 2 }, chunk.Payload.ToArray());
        }

        using (var chunk = await ReadRequiredChunkAsync(receiver))
        {
            Assert.Equal(new byte[] { 3 }, chunk.Payload.ToArray());
        }

        Assert.Null(await receiver.ReadChunkAsync(CancellationToken.None).AsTask().WaitAsync(Timeout));
        Assert.Throws<ObjectDisposedException>(() => _ = frame1.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = frame2.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = frame3.Memory);
    }

    [Fact]
    public async Task CompleteInbound_WithOneBufferedChunk_DisposesPayloadWhenChunkIsDisposed()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(42_002, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        var frame = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 99 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        streams.CompleteInbound(handle.StreamId);

        using (var chunk = await ReadRequiredChunkAsync(receiver))
        {
            Assert.Equal(new byte[] { 99 }, chunk.Payload.ToArray());
        }

        Assert.Throws<ObjectDisposedException>(() => _ = frame.Memory);
    }

    [Fact]
    public async Task CompleteInbound_WithBufferedChunks_ReadChunkAsyncDrainsBeforeReturningNull()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(42_003, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        var frame = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 7, 8 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        streams.CompleteInbound(handle.StreamId);

        using (var chunk = await ReadRequiredChunkAsync(receiver))
        {
            Assert.Equal(new byte[] { 7, 8 }, chunk.Payload.ToArray());
        }

        Assert.Null(await receiver.ReadChunkAsync(CancellationToken.None).AsTask().WaitAsync(Timeout));
    }

    private static async Task<RpcStreamChunk> ReadRequiredChunkAsync(RpcStreamReceiver receiver) =>
        Assert.IsType<RpcStreamChunk>(
            await receiver.ReadChunkAsync(CancellationToken.None).AsTask().WaitAsync(Timeout));

    private static RpcStreamManager CreateStreamManager() =>
        new(new MessagePackRpcSerializer(), SendNoopAsync, exceptionTransformer: null);

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
