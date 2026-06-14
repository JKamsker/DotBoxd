using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression for <see cref="RpcStreamManager.Stop"/>. Connection teardown must release the pooled
/// <see cref="ShaRPC.Core.Buffers.Payload"/> buffers that are still buffered inside inbound stream
/// receivers. <c>Stop</c> only <c>Complete</c>d each receiver (completing the channel without draining
/// it), so any chunk a consumer had not yet read kept its rented buffer alive instead of returning it
/// to the pool. Draining (Abort) on stop fixes the leak.
/// </summary>
public sealed class StreamingStopDrainRegressionTests
{
    [Fact]
    public void Stop_DrainsBufferedInboundChunk_DisposingPooledPayload()
    {
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(31_000, RpcStreamKind.Binary);
        streams.RegisterInboundResponse(handle, CancellationToken.None);
        var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 1, 2, 3 });

        // The item is buffered inside the receiver's channel; no consumer reads it.
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));
        Assert.Equal(3, frame.Memory.Slice(MessageFramer.HeaderSize).Length);

        streams.Stop();

        // Tearing down the connection must drain the buffered chunk and return its rented buffer to the
        // pool. With the leaking Complete-only path the chunk stays buffered and the payload is alive.
        Assert.Throws<ObjectDisposedException>(() => _ = frame.Memory);
    }

    private static RpcStreamManager CreateStreamManager() =>
        new(new MessagePackRpcSerializer(), SendNoopAsync, exceptionTransformer: null);

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
