using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Streaming.Core;
using Xunit;
using static DotBoxD.Services.Tests.Streaming.Core.StreamingProtocolRegressionTestSupport;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class StreamingInboundStreamLimitTests
{
    [Fact]
    public async Task OversizedRequestStreamDeclaration_ReturnsProtocolErrorBeforeRegisteringReceivers()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(serializer, SendNoopAsync, exceptionTransformer: null);
        var errors = new List<string>();
        var inbound = CreateInbound(serializer, streams, new RpcPeerOptions { MaxInboundStreamsPerRequest = 2 }, errors);
        using var frame = CreateRequestFrame(
            serializer,
            20,
            new[]
            {
                new RpcStreamHandle(800, RpcStreamKind.Binary),
                new RpcStreamHandle(801, RpcStreamKind.Items),
                new RpcStreamHandle(802, RpcStreamKind.Binary),
            });

        var accepted = await inbound.AcceptRequestAsync(frame, 20, CancellationToken.None);

        Assert.False(accepted);
        Assert.Single(errors, error => error.Contains("Request declares 3 inbound streams"));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public async Task PeerInboundStreamCapacity_ReturnsProtocolErrorBeforeRegisteringNewReceiver()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            SendNoopAsync,
            exceptionTransformer: null,
            maxInboundStreamsPerPeer: 1);
        streams.RegisterInboundResponse(new RpcStreamHandle(810, RpcStreamKind.Binary), CancellationToken.None);
        var errors = new List<string>();
        var inbound = CreateInbound(serializer, streams, new RpcPeerOptions(), errors);
        using var frame = CreateRequestFrame(
            serializer,
            21,
            new[] { new RpcStreamHandle(811, RpcStreamKind.Binary) });

        var accepted = await inbound.AcceptRequestAsync(frame, 21, CancellationToken.None);

        Assert.False(accepted);
        Assert.Single(errors, error => error.Contains("Inbound stream capacity exceeded"));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(1, streams.InboundReceiverCount);

        streams.RemoveInbound(810);
    }

    [Fact]
    public async Task PeerInboundStreamCapacity_ReturnsProtocolErrorBeforeRegisteringPartialRequest()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            SendNoopAsync,
            exceptionTransformer: null,
            maxInboundStreamsPerPeer: 1);
        var errors = new List<string>();
        var inbound = CreateInbound(serializer, streams, new RpcPeerOptions(), errors);
        using var frame = CreateRequestFrame(
            serializer,
            22,
            new[]
            {
                new RpcStreamHandle(812, RpcStreamKind.Binary),
                new RpcStreamHandle(813, RpcStreamKind.Items),
            });

        var accepted = await inbound.AcceptRequestAsync(frame, 22, CancellationToken.None);

        Assert.False(accepted);
        Assert.Single(errors, error => error.Contains("2 requested"));
        Assert.Equal(0, inbound.ActiveInboundCount);
        Assert.Equal(0, streams.InboundReceiverCount);
    }

    [Fact]
    public void PeerInboundStreamCapacity_ReleasesSlotAfterCompletion()
    {
        var serializer = new MessagePackRpcSerializer();
        var streams = new RpcStreamManager(
            serializer,
            SendNoopAsync,
            exceptionTransformer: null,
            maxInboundStreamsPerPeer: 1);
        var first = new RpcStreamHandle(814, RpcStreamKind.Binary);
        var second = new RpcStreamHandle(815, RpcStreamKind.Items);

        streams.RegisterInboundResponse(first, CancellationToken.None);
        streams.CompleteInbound(first.StreamId);
        var receiver = streams.RegisterInboundResponse(second, CancellationToken.None);

        Assert.Equal(1, streams.InboundReceiverCount);
        Assert.Equal(second, receiver.Handle);

        streams.RemoveInbound(second.StreamId);
    }

    private static RpcPeerInboundDispatcher CreateInbound(
        MessagePackRpcSerializer serializer,
        RpcStreamManager streams,
        RpcPeerOptions options,
        List<string> errors) =>
        new(
            serializer,
            options,
            streams,
            SendNoopAsync,
            (id, type, message, _) => errors.Add($"{id}:{type}:{message}"),
            dispatchError: static (_, _) => { });

    private static DotBoxD.Services.Buffers.Payload CreateRequestFrame(
        MessagePackRpcSerializer serializer,
        int messageId,
        RpcStreamHandle[] streams) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = "Svc",
                MethodName = "Upload",
                Streams = streams,
            },
            ReadOnlySpan<byte>.Empty);
}
