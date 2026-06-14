using System.Runtime.CompilerServices;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Client;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Streaming;
using ShaRPC.Serializers.MessagePack;
using Xunit;

namespace ShaRPC.Tests;

public sealed class StreamingProtocolRegressionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

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

        await Assert.ThrowsAsync<ShaRpcRemoteException>(() =>
            invoker.InvokeAsync<int>("Svc", "Bad", CancellationToken.None));

        Assert.Equal(0, streams.InboundReceiverCount);

        Task SendAndCompleteErrorAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var messageId, out var type));
            if (type != MessageType.Request)
            {
                return Task.CompletedTask;
            }

            var response = MessageFramer.FrameMessage(
                serializer,
                messageId,
                MessageType.Error,
                new RpcResponse
                {
                    MessageId = messageId,
                    IsSuccess = false,
                    ErrorMessage = "failed",
                    ErrorType = "Remote",
                    Stream = new RpcStreamHandle(604, RpcStreamKind.Binary),
                },
                ReadOnlySpan<byte>.Empty);
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

    [Fact]
    public async Task DuplicateInboundStreamHandles_ReturnProtocolErrorWithoutLeakingRequest()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        MessageType? sentType = null;
        var protocolErrors = new List<string>();
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions(),
            streams,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                sentType = type;
                return Task.CompletedTask;
            },
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });
        using var frame = MessageFramer.FrameMessage(
            serializer,
            10,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 10,
                ServiceName = "Svc",
                MethodName = "Upload",
                Streams = new[]
                {
                    new RpcStreamHandle(700, RpcStreamKind.Binary),
                    new RpcStreamHandle(700, RpcStreamKind.Items),
                },
            },
            ReadOnlySpan<byte>.Empty);

        var accepted = await inbound.AcceptRequestAsync(frame, 10, CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(MessageType.Error, sentType);
        Assert.Single(protocolErrors, error => error.Contains("Duplicate inbound stream id '700'."));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task ActiveInboundStreamReuse_ReturnsProtocolErrorWithoutAliasingReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        streams.RegisterInbound(new[] { new RpcStreamHandle(701, RpcStreamKind.Binary) }, CancellationToken.None);
        MessageType? sentType = null;
        var protocolErrors = new List<string>();
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions(),
            streams,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                sentType = type;
                return Task.CompletedTask;
            },
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });
        using var frame = MessageFramer.FrameMessage(
            serializer,
            11,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 11,
                ServiceName = "Svc",
                MethodName = "Upload",
                Streams = new[] { new RpcStreamHandle(701, RpcStreamKind.Binary) },
            },
            ReadOnlySpan<byte>.Empty);

        var accepted = await inbound.AcceptRequestAsync(frame, 11, CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(MessageType.Error, sentType);
        Assert.Single(protocolErrors, error => error.Contains("Inbound stream id '701' is already active."));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(1, streams.InboundReceiverCount);
        streams.RemoveInbound(701);
    }

    [Fact]
    public void DuplicateOutboundHandles_DoNotLeavePartialSenderState()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var handle = new RpcStreamHandle(10, RpcStreamKind.Binary);
        streams.ReserveOutbound(handle.StreamId);
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 1 })),
            RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 2 })),
        };

        Assert.Throws<ShaRpcProtocolException>(() =>
            streams.RegisterOutbound(attachments, CancellationToken.None));

        Assert.Equal(0, streams.OutboundSenderCount);
        using var lateCredit = RpcRawFrame.FrameInt32(handle.StreamId, MessageType.StreamCredit, 1);
        Assert.True(streams.TryAddCredit(lateCredit));
        Assert.Equal(0, streams.PendingCreditCount);
    }

    [Fact]
    public async Task FailedStreamRegistration_ReleasesPendingRequestSlot()
    {
        var serializer = new MessagePackRpcSerializer();
        RpcPeerOutboundInvoker? invoker = null;
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var sends = 0;
        invoker = new RpcPeerOutboundInvoker(
            serializer,
            new RpcPeerOptions { MaxPendingRequests = 1, RequestTimeout = TestTimeout },
            ensureStarted: static () => { },
            SendAndCompleteAsync,
            streams);
        var handle = invoker.ReserveStream(RpcStreamKind.Binary);
        var attachments = new[]
        {
            RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 1 })),
            RpcStreamAttachment.FromStream(handle, new MemoryStream(new byte[] { 2 })),
        };

        await Assert.ThrowsAsync<ShaRpcProtocolException>(() =>
            invoker.InvokeAsync<(RpcStreamHandle, RpcStreamHandle), int>(
                "Svc",
                "Upload",
                (handle, handle),
                attachments));

        Assert.Equal(0, sends);
        await invoker.InvokeAsync("Svc", "Ping").WaitAsync(TestTimeout);
        Assert.Equal(1, sends);

        Task SendAndCompleteAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            sends++;
            Assert.True(MessageFramer.TryReadFrameHeader(frame, out var messageId, out _));
            var response = MessageFramer.FrameMessage(
                serializer,
                messageId,
                MessageType.Response,
                new RpcResponse { MessageId = messageId, IsSuccess = true },
                ReadOnlySpan<byte>.Empty);
            if (!invoker!.TryCompleteResponse(messageId, response))
            {
                response.Dispose();
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task MalformedInboundStreamHandle_ReturnsProtocolErrorWithoutLeakingRequest()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        MessageType? sentType = null;
        var protocolErrors = new List<string>();
        var inbound = new RpcPeerInboundDispatcher(
            serializer,
            new RpcPeerOptions(),
            streams,
            (frame, ct) =>
            {
                Assert.True(MessageFramer.TryReadFrameHeader(frame, out _, out var type));
                sentType = type;
                return Task.CompletedTask;
            },
            (id, type, message, _) => protocolErrors.Add($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });
        using var frame = MessageFramer.FrameMessage(
            serializer,
            9,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 9,
                ServiceName = "Svc",
                MethodName = "Upload",
                Streams = new[] { new RpcStreamHandle(0, RpcStreamKind.Binary) },
            },
            ReadOnlySpan<byte>.Empty);

        var accepted = await inbound.AcceptRequestAsync(frame, 9, CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(MessageType.Error, sentType);
        Assert.Single(protocolErrors, error => error.Contains("Stream id must not be zero."));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    private static RpcPeerInboundDispatcher CreateInbound(
        ISerializer serializer,
        RpcStreamManager streams) =>
        new(
            serializer,
            new RpcPeerOptions(),
            streams,
            SendNoopAsync,
            protocolError: static (_, _, _, _) => { },
            dispatchError: static (_, _) => { });

    private static Task SendNoopAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
        Task.CompletedTask;

    private static async IAsyncEnumerable<int> BlockingItems(
        TaskCompletionSource started,
        TaskCompletionSource canceled,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        started.TrySetResult();
        try
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            yield return 1;
        }
        finally
        {
            canceled.TrySetResult();
        }
    }
}
