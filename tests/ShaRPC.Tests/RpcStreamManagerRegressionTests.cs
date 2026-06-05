using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

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

        await Assert.ThrowsAsync<ShaRpcConnectionException>(() =>
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

        await Assert.ThrowsAsync<ShaRpcConnectionException>(() =>
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

    private static RpcStreamManager CreateStreamManager()
    {
        var serializer = new MessagePackRpcSerializer();
        return new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
