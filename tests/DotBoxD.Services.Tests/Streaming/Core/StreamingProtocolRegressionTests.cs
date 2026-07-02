using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using DotBoxD.Services.Streaming.Remote;
using DotBoxD.Services.Tests.Support;
using Xunit;
using static DotBoxD.Services.Tests.Streaming.Core.StreamingProtocolRegressionTestSupport;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingProtocolRegressionTests
{
    [Fact]
    public async Task RequestCancel_DoesNotCancelOutboundStreamWithSameId()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var inbound = CreateInbound(serializer, streams);
        var outboundInvoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        var processor = new RpcPeerFrameProcessor(
            inbound,
            outboundInvoker,
            streams,
            protocolError: static (_, _, _, _) => { });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = new RpcStreamHandle(7, RpcStreamKind.Items);
        streams.ReserveOutbound(handle.StreamId);

        await using var outbound = streams.RegisterOutbound(
            new[] { RpcStreamAttachment.FromAsyncEnumerable(handle, BlockingItems(started, canceled)) },
            CancellationToken.None);
        outbound.Start();
        await started.Task.WaitAsync(TestTimeout);

        using (var requestCancel = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.Cancel,
            ReadOnlySpan<byte>.Empty))
        {
            Assert.True(await processor.ShouldDisposeAsync(requestCancel, CancellationToken.None));
        }

        Assert.False(canceled.Task.IsCompleted);

        using (var streamCancel = MessageFramer.FrameToPayload(
            handle.StreamId,
            MessageType.StreamCancel,
            ReadOnlySpan<byte>.Empty))
        {
            Assert.True(await processor.ShouldDisposeAsync(streamCancel, CancellationToken.None));
        }

        await canceled.Task.WaitAsync(TestTimeout);
    }

    [Fact]
    public void UnknownStreamingResponse_DoesNotRegisterReceiverOrSendCredit()
    {
        var serializer = new MessagePackRpcSerializer();
        var creditFrames = 0;
        var streams = new RpcStreamManager(
            serializer,
            (_, _) =>
            {
                creditFrames++;
                return Task.CompletedTask;
            },
            exceptionTransformer: null);
        var invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions(),
            ensureStarted: static () => { },
            SendNoopAsync,
            streams);
        using var frame = MessageFramer.FrameMessage(
            serializer,
            123,
            MessageType.Response,
            new RpcResponse
            {
                MessageId = 123,
                IsSuccess = true,
                Stream = new RpcStreamHandle(456, RpcStreamKind.Binary),
            },
            ReadOnlySpan<byte>.Empty);

        Assert.False(invoker.TryCompleteResponse(123, frame));
        Assert.Equal(0, streams.InboundReceiverCount);
        Assert.Equal(0, streams.PendingCreditCount);
        Assert.Equal(0, creditFrames);
    }

    [Fact]
    public void UnclaimedStreamingResponse_DisposeCancelsAndRemovesReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(600, RpcStreamKind.Binary);
        var receiver = streams.RegisterInboundResponse(handle, CancellationToken.None);
        var frame = MessageFramer.FrameMessage(
            serializer,
            1,
            MessageType.Response,
            new RpcResponse
            {
                MessageId = 1,
                IsSuccess = true,
                Stream = handle,
            },
            ReadOnlySpan<byte>.Empty);
        var response = new ReceivedResponse(
            new RpcResponse { MessageId = 1, IsSuccess = true, Stream = handle },
            ReadOnlyMemory<byte>.Empty,
            frame,
            receiver);

        response.Dispose();

        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task ReceiverCancel_CleansUpLocalState_WhenCancelFrameSendFails()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            static (_, _) => throw new InvalidOperationException("send failed"),
            exceptionTransformer: null);
        var receiver = streams.RegisterInboundResponse(
            new RpcStreamHandle(601, RpcStreamKind.Binary),
            CancellationToken.None);

        await receiver.CancelAsync();

        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task ErrorResponseWithStream_DisposesRegisteredReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcPeerOutboundInvoker? invoker = null;
        var streams = new RpcStreamManager(serializer, SendAndCompleteErrorAsync, exceptionTransformer: null);
        invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { RequestTimeout = TestTimeout },
            ensureStarted: static () => { },
            SendAndCompleteErrorAsync,
            streams);

        await Assert.ThrowsAsync<ServiceProtocolException>(() =>
            invoker.InvokeAsync<int>("Svc", "Bad", CancellationToken.None));

        Assert.Equal(0, streams.InboundReceiverCount);

        Task SendAndCompleteErrorAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var messageId, out var type));
            if (type != MessageType.Request)
            {
                return Task.CompletedTask;
            }

            var response = FrameErrorResponseWithStream(serializer, messageId);
            if (!invoker!.TryCompleteResponse(messageId, response))
            {
                response.Dispose();
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task UnknownCredits_AreIgnoredInsteadOfBufferedForever()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);

        for (var id = 1; id <= 100; id++)
        {
            using var credit = RpcRawFrame.FrameInt32(id, MessageType.StreamCredit, 1);
            Assert.True(streams.TryAddCredit(credit));
        }

        Assert.Equal(0, streams.PendingCreditCount);

        var handle = new RpcStreamHandle(500, RpcStreamKind.Binary);
        streams.ReserveOutbound(handle.StreamId);
        using (var earlyCredit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 2))
        {
            Assert.True(streams.TryAddCredit(earlyCredit));
        }

        Assert.Equal(1, streams.PendingCreditCount);
        using var data = new MemoryStream(new byte[] { 1 });
        var attachment = RpcStreamAttachment.FromStream(handle, data);
        var outbound = streams.RegisterOutbound(new[] { attachment }, CancellationToken.None);

        Assert.Equal(0, streams.PendingCreditCount);
        Assert.Equal(1, streams.OutboundSenderCount);
        await outbound.DisposeAsync();
    }

    [Fact]
    public async Task ResponseStream_UsesLocallyReservedId_NotRemoteRequestId()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var existing = streams.ReserveOutbound(RpcStreamKind.Binary);
        var context = new RpcStreamingContext(streams, serializer, CancellationToken.None);

        context.SetResponse(new MemoryStream(new byte[] { 1, 2, 3 }));
        var response = context.Response;
        Assert.NotNull(response);

        Assert.NotEqual(existing.StreamId, response!.Handle.StreamId);
        await using var outbound = streams.RegisterOutbound(new[] { response }, CancellationToken.None);
        Assert.Equal(1, streams.OutboundSenderCount);

        streams.RemoveOutbound(existing.StreamId);
    }

    private static Payload FrameErrorResponseWithStream(MessagePackRpcSerializer serializer, int messageId)
        => RpcEnvelopeTestFrames.FrameErrorResponse(
            serializer,
            frameMessageId: messageId,
            messageType: MessageType.Error,
            envelopeMessageId: messageId,
            isSuccess: false,
            errorMessage: "failed",
            errorType: "Remote",
            stream: new RpcStreamHandle(604, RpcStreamKind.Binary));
}
