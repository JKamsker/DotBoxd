using ShaRPC.Core;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingPipeBridgeCancelRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CreateReadablePipe_WhenCanceled_CancelsReceiverAndRemovesItFromManager()
    {
        // Arrange
        var streams = CreateStreamManager();
        var handle = new RpcStreamHandle(200, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);

        // Feed one item so the pump is not blocked waiting for a chunk when cancellation
        // fires. After reading the item it calls FlushAsync(ct), where the already-canceled
        // token surfaces as OperationCanceledException and lands in the catch block.
        using var frame = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamItem,
            new byte[] { 0xAB });
        Assert.True(streams.TryAcceptItem(handle.StreamId, frame));

        using var cts = new CancellationTokenSource();
        // Cancel before wiring the pipe so the token is already signaled when the pump
        // first calls ReadChunkAsync(ct) or FlushAsync(ct).
        cts.Cancel();
        var pipe = RpcPipeBridge.CreateReadablePipe(receiver, cts.Token);

        // Act -- give the fire-and-forget PumpAsync task time to observe the cancellation
        // and run its catch block.
        await Task.Delay(200);

        // Assert -- receiver.Cancel() must have been called, removing the receiver from the
        // manager. Under the bug the catch block skips receiver.Cancel(), so the receiver
        // stays in _receivers and InboundReceiverCount remains 1 instead of 0.
        Assert.Equal(0, streams.InboundReceiverCount);

        // Cleanup
        await pipe.Reader.CompleteAsync();
    }

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(
            serializer,
            static (_, _) => Task.CompletedTask,
            exceptionTransformer: null);
    }
}
