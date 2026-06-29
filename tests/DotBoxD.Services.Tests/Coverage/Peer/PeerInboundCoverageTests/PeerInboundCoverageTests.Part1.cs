using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed partial class PeerInboundCoverageTests
{
    [Fact]
    public async Task HandlerThrows_WithTransformer_SurfacesMappedError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;

        await using var peer = RpcPeer
            .Over(
                serverConnection,
                serializer,
                new RpcPeerOptions
                {
                    RequestTimeout = ShortTimeout,
                    ExceptionTransformer = ex => new RpcErrorInfo(ex.Message, "MyDomainError"),
                })
            .Provide((IServiceDispatcher)new ThrowingDispatcher("validation-failed"))
            .Start();

        using var requestFrame = CreateRequestFrame(serializer, 41, ThrowingDispatcher.Service, "Boom");
        await client.SendAsync(requestFrame.Memory);

        var response = await ReadErrorResponseAsync(client, serializer, expectedMessageId: 41);
        Assert.Equal("MyDomainError", response.ErrorType);
        Assert.Equal("validation-failed", response.ErrorMessage);
    }

    [Fact]
    public async Task RequestEnvelopeMessageIdMismatch_ReturnsProtocolError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        var protocolError = new TaskCompletionSource<RpcProtocolErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var peer = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = ShortTimeout })
            .Provide((IServiceDispatcher)new EchoDispatcher())
            .Start();
        peer.ProtocolError += (_, args) => protocolError.TrySetResult(args);

        using var requestFrame = CreateRequestFrame(
            serializer,
            frameMessageId: 41,
            envelopeMessageId: 99,
            EchoDispatcher.Service,
            "Call");
        await client.SendAsync(requestFrame.Memory);

        var response = await ReadErrorResponseAsync(client, serializer, expectedMessageId: 41);
        var args = await protocolError.Task.WaitAsync(ShortTimeout);

        Assert.Equal(RpcErrorTypes.ProtocolError, response.ErrorType);
        Assert.Contains("message id", response.ErrorMessage);
        Assert.Equal(41, args.MessageId);
        Assert.Equal(MessageType.Request, args.MessageType);
        Assert.Contains("message id", args.Message);
    }

    // ---- DispatchError event when the error response itself cannot be sent (280, dispatch path) -

    [Fact]
    public async Task HandlerThrows_AndErrorSendFails_RaisesDispatchErrorEvent()
    {
        var serializer = NewSerializer();
        var dispatchError = new TaskCompletionSource<RpcDispatchErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // The handler throws, so the dispatcher tries to send an Error frame; the channel fails every
        // send, so the dispatcher reports the failure through the DispatchError event before swallowing
        // the best-effort send fault.
        await using var connection = new SendFailingConnection();
        connection.Enqueue(CreateRequestFrame(serializer, 91, ThrowingDispatcher.Service, "Boom"));

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions { InboundQueueCapacity = null, RequestTimeout = ShortTimeout })
            .Provide((IServiceDispatcher)new ThrowingDispatcher("kaboom"))
            .Start();
        peer.DispatchError += (_, args) => dispatchError.TrySetResult(args);

        var args = await dispatchError.Task.WaitAsync(ShortTimeout);

        Assert.Equal(91, args.MessageId);
        Assert.Equal(ThrowingDispatcher.Service, args.ServiceName);
        Assert.Equal("Boom", args.MethodName);
        Assert.IsType<SendFailureException>(args.Error);
    }

    // ---- Cancel frame cancels an in-flight inbound request (RpcPeerFrameProcessor Cancel path) -

    [Fact]
    public async Task CancelFrame_CancelsInFlightInboundRequest()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new CancelAwareDispatcher();

        connection.Enqueue(CreateRequestFrame(serializer, 12, CancelAwareDispatcher.Service, "Wait"));

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions { InboundQueueCapacity = null, RequestTimeout = TimeSpan.FromMinutes(5) })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.Started.Task.WaitAsync(ShortTimeout);

        // A Cancel control frame (header only, no envelope) for the same message id cancels the linked
        // CTS feeding the in-flight handler.
        using var cancelFrame = MessageFramer.FrameToPayload(12, MessageType.Cancel, ReadOnlySpan<byte>.Empty);
        connection.Enqueue(CopyFrame(cancelFrame));

        await dispatcher.Canceled.Task.WaitAsync(ShortTimeout);
    }

    // ---- StopAsync awaits in-flight inline (unbounded) dispatch on dispose (154-156, ObserveShutdown) --

}
