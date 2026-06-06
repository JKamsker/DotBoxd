using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression: <see cref="RpcStreamManager.CompleteInbound(int)"/> calls
/// <see cref="RpcStreamReceiver.Complete"/> which completes the channel writer but does NOT drain
/// buffered chunks. Any <see cref="ShaRPC.Core.Buffers.Payload"/> objects already buffered in the
/// receiver's channel are therefore NOT disposed, meaning their rented <c>ArrayPool</c> buffers are
/// not returned until the consumer reads and disposes each chunk — or until <c>Stop()</c> eventually
/// calls <c>Abort()</c> on teardown.
///
/// The expected (fixed) behaviour: <c>CompleteInbound</c> should drain buffered chunks and dispose
/// their backing <c>Payload</c> objects, just as <see cref="RpcStreamReceiver.Abort"/> does.
///
/// These tests are RED: they assert the expected post-fix behaviour. They fail against the current
/// production code because <c>Complete</c> does not call <c>DrainChunks()</c>.
/// </summary>
public sealed class StreamingCompleteChunkDrainTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Feed 3 items into a receiver, then call CompleteInbound (simulating a StreamComplete frame
    /// arriving before the consumer has read anything). The 3 pooled Payload buffers must be
    /// disposed (drained) by CompleteInbound itself.
    ///
    /// Currently FAILS because Complete() does not drain: frame.Memory succeeds instead of
    /// throwing ObjectDisposedException.
    /// </summary>
    [Fact]
    public void CompleteInbound_WithBufferedChunks_DrainsAndDisposesPayloads()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(42_001, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);

        var frame1 = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 1 });
        var frame2 = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 2 });
        var frame3 = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 3 });

        // Buffer all 3 items. The consumer has not read any of them yet.
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame1));
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame2));
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame3));

        // Simulate the remote sending StreamComplete.
        // With the bug: Complete() completes the channel writer but does not drain the 3 buffered
        // chunks, so their Payload objects remain undisposed.
        streams.CompleteInbound(handle.StreamId);

        // Expected (post-fix): CompleteInbound must drain and dispose the buffered chunks.
        // A disposed Payload throws ObjectDisposedException on .Memory access.
        //
        // CURRENTLY FAILS: frame.Memory does not throw, proving the buffer was NOT returned.
        Assert.Throws<ObjectDisposedException>(() => _ = frame1.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = frame2.Memory);
        Assert.Throws<ObjectDisposedException>(() => _ = frame3.Memory);
    }

    /// <summary>
    /// Same scenario with a single buffered chunk to keep the failure signal unambiguous.
    /// </summary>
    [Fact]
    public void CompleteInbound_WithOneBufferedChunk_DrainsAndDisposesPayload()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(42_002, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);

        var frame = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 99 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        streams.CompleteInbound(handle.StreamId);

        // CURRENTLY FAILS: the Payload is still alive because Complete() did not drain the channel.
        Assert.Throws<ObjectDisposedException>(() => _ = frame.Memory);
    }

    /// <summary>
    /// After CompleteInbound drains the buffered chunks, ReadChunkAsync must return null
    /// immediately (the channel is completed and empty) rather than returning the un-disposed chunks.
    ///
    /// CURRENTLY FAILS: ReadChunkAsync returns the first buffered chunk instead of null, proving
    /// that the chunks were not drained by CompleteInbound.
    /// </summary>
    [Fact]
    public async Task CompleteInbound_WithBufferedChunks_ReadChunkAsyncReturnsNull()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(42_003, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        var frame = MessageFramer.FrameToPayload(handle.StreamId, MessageType.StreamItem, new byte[] { 7, 8 });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        // Simulate StreamComplete arriving before the consumer reads.
        streams.CompleteInbound(handle.StreamId);

        // Expected (post-fix): the channel is both completed AND empty (drained), so
        // ReadChunkAsync should return null.
        //
        // CURRENTLY FAILS: ReadChunkAsync returns the un-drained buffered chunk instead of null.
        var chunk = await receiver.ReadChunkAsync(CancellationToken.None).AsTask().WaitAsync(Timeout);
        Assert.Null(chunk);
    }

    private static RpcStreamManager CreateStreamManager() =>
        new(new MessagePackRpcSerializer(), SendNoopAsync, exceptionTransformer: null);

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
