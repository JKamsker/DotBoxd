using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingOutboundReservationRegressionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task StreamedInvoke_PreCanceledBeforePendingReservation_ReleasesStreamReservation()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 1 })),
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            invoker.InvokeAsync<RpcStreamHandle, int>(
                "Svc",
                "Upload",
                handle,
                attachments,
                cts.Token));

        AssertNoPendingCreditForReleasedReservation(streams, handle.StreamId);
    }

    [Fact]
    public async Task StreamedInvoke_MaxPendingBeforeStreamRegistration_ReleasesStreamReservation()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var sentRequest = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { MaxPendingRequests = 1, RequestTimeout = Timeout },
            ensureStarted: static () => { },
            SendAndHoldAsync,
            streams);
        var pending = invoker.InvokeAsync("Svc", "Hold");
        var pendingId = await sentRequest.Task.WaitAsync(Timeout);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 1 })),
        };

        await Assert.ThrowsAsync<ShaRpcException>(() =>
            invoker.InvokeAsync<RpcStreamHandle, int>(
                "Svc",
                "Upload",
                handle,
                attachments));

        AssertNoPendingCreditForReleasedReservation(streams, handle.StreamId);
        CompleteSuccess(invoker, serializer, pendingId);
        await pending.WaitAsync(Timeout);

        Task SendAndHoldAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var messageId, out _));
            sentRequest.TrySetResult(messageId);
            return Task.CompletedTask;
        }
    }

    private static void CompleteSuccess(
        RpcPeerOutboundInvoker invoker,
        MessagePackRpcSerializer serializer,
        int messageId)
    {
        var response = MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Response,
            new RpcResponse { MessageId = messageId, IsSuccess = true },
            ReadOnlySpan<byte>.Empty);
        if (!invoker.TryCompleteResponse(messageId, response))
        {
            response.Dispose();
        }
    }

    private static void AssertNoPendingCreditForReleasedReservation(
        RpcStreamManager streams,
        int streamId)
    {
        using var credit = RpcRawFrame.FrameInt32(streamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(credit));
        Assert.Equal(0, streams.PendingCreditCount);
    }

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;
}
