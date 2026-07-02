using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Client;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using DotBoxD.Services.Streaming.Frames;
using Xunit;
using static DotBoxD.Services.Tests.Streaming.Core.StreamingProtocolRegressionTestSupport;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingProtocolValidationRegressionTests
{
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

        Assert.Throws<ServiceProtocolException>(() =>
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

        await Assert.ThrowsAsync<ServiceProtocolException>(() =>
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
        Assert.Single(protocolErrors, error => error.Contains("Stream id must be positive."));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task NegativeInboundStreamHandle_ReturnsProtocolErrorWithoutRegisteringReceiver()
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
            12,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = 12,
                ServiceName = "Svc",
                MethodName = "Upload",
                Streams = new[] { new RpcStreamHandle(-7, RpcStreamKind.Binary) },
            },
            ReadOnlySpan<byte>.Empty);

        var accepted = await inbound.AcceptRequestAsync(frame, 12, CancellationToken.None);

        Assert.False(accepted);
        Assert.Equal(MessageType.Error, sentType);
        Assert.Single(protocolErrors, error => error.Contains("Stream id", StringComparison.Ordinal));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public void RegisterInbound_RejectsNegativeStreamHandle()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);

        var ex = Assert.Throws<ServiceProtocolException>(() =>
            streams.RegisterInbound(
                new[] { new RpcStreamHandle(-7, RpcStreamKind.Binary) },
                CancellationToken.None));

        Assert.Contains("Stream id", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, streams.InboundReceiverCount);
    }
}
