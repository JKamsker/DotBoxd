using System.Buffers.Binary;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Peer;

public sealed class RpcPeerProtocolRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task DisposeAsync_FailsInFlightCallsWithConnectionException()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var call = client.InvokeAsync<int>("MissingService", "NeverCompletes");
        using (await serverConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)))
        {
        }

        await client.DisposeAsync();

        await Assert.ThrowsAsync<ServiceConnectionException>(
            () => call.WaitAsync(TimeSpan.FromSeconds(1)));
        await serverConnection.DisposeAsync();
    }

    [Fact]
    public async Task MalformedResponse_FaultsMatchingCall_AndPeerSurvives()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = NewSerializer();

        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var call = client.InvokeAsync<int>("Service", "Method");
        var messageId = await ReadRequestIdAsync(serverConnection);
        await SendMalformedResponseAsync(serverConnection, messageId);

        await Assert.ThrowsAsync<ServiceProtocolException>(
            () => call.WaitAsync(TimeSpan.FromSeconds(1)));

        var secondCall = client.InvokeAsync<int>("Service", "Method");
        var secondMessageId = await ReadRequestIdAsync(serverConnection);
        await SendSuccessResponseAsync(serverConnection, serializer, secondMessageId, 123);

        Assert.Equal(123, await secondCall.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ServiceException_DoesNotLeakRawExceptionDetails()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var server = RpcPeer
            .Over(serverConnection, NewSerializer())
            .Provide((IServiceDispatcher)new ThrowingDispatcher())
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(
            () => client.InvokeAsync<int>(ThrowingDispatcher.Service, "Throw"));

        Assert.Equal("Internal error.", ex.Message);
        Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
        Assert.DoesNotContain("C:\\secret", ex.Message);
    }

    [Fact]
    public async Task ServiceException_WithExposingTransformer_SurfacesMessageAndType()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions { ExceptionTransformer = ex => RpcErrorInfo.FromException(ex) })
            .Provide((IServiceDispatcher)new ThrowingDispatcher())
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(
            () => client.InvokeAsync<int>(ThrowingDispatcher.Service, "Throw"));

        // With the server opting in, the handler's real message and exception type reach the caller.
        Assert.Equal("Internal path C:\\secret\\db.txt", ex.Message);
        Assert.Equal(nameof(InvalidOperationException), ex.RemoteExceptionType);
    }

    [Fact]
    public async Task ServiceException_WithSelectiveTransformer_MapsToSafeError()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions
                {
                    ExceptionTransformer = ex => ex is InvalidOperationException
                        ? new RpcErrorInfo("The request could not be processed.", "AppRequestRejected")
                        : null,
                })
            .Provide((IServiceDispatcher)new ThrowingDispatcher())
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(
            () => client.InvokeAsync<int>(ThrowingDispatcher.Service, "Throw"));

        // The transformer maps the exception to a safe, caller-facing message and a custom type, and
        // never exposes the raw internal path.
        Assert.Equal("The request could not be processed.", ex.Message);
        Assert.Equal("AppRequestRejected", ex.RemoteExceptionType);
        Assert.DoesNotContain("C:\\secret", ex.Message);
    }

    [Fact]
    public async Task ServiceException_WithTransformerReturningNull_StaysOpaque()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions { ExceptionTransformer = _ => null })
            .Provide((IServiceDispatcher)new ThrowingDispatcher())
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(
            () => client.InvokeAsync<int>(ThrowingDispatcher.Service, "Throw"));

        // Returning null keeps the secure opaque default for that exception.
        Assert.Equal("Internal error.", ex.Message);
        Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
    }

    [Fact]
    public async Task Timeout_SendsCancelFrameAndRemovesPendingRequest()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var client = RpcPeer
            .Over(
                clientConnection,
                NewSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) })
            .Start();

        var call = client.InvokeAsync<int>("Service", "Slow");
        var messageId = await ReadRequestIdAsync(serverConnection);

        await Assert.ThrowsAsync<ServiceTimeoutException>(
            () => call.WaitAsync(TimeSpan.FromSeconds(2)));

        using var cancelFrame = await serverConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(MessageFramer.TryReadFrameHeader(
            cancelFrame.Memory,
            out var cancelId,
            out var messageType));
        Assert.Equal(messageId, cancelId);
        Assert.Equal(MessageType.Cancel, messageType);
    }

    [Fact]
    public async Task InboundCancelFrame_CancelsInFlightDispatch_SendsNoResponse_AndPeerSurvives()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = NewSerializer();
        var cancellable = new CancellableDispatcher();

        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Provide((IServiceDispatcher)cancellable)
            .Provide((IServiceDispatcher)new PingDispatcher())
            .Start();

        // Send an inbound request and wait until its handler is mid-flight.
        using (var requestFrame = CreateRequestFrame(serializer, 1, CancellableDispatcher.Service, "Wait"))
        {
            await clientConnection.SendAsync(requestFrame.Memory);
        }

        await cancellable.Entered.WaitAsync(TimeSpan.FromSeconds(1));

        // A Cancel frame for that message id must cancel the in-flight dispatch.
        using (var cancelFrame = MessageFramer.FrameToPayload(1, MessageType.Cancel, ReadOnlySpan<byte>.Empty))
        {
            await clientConnection.SendAsync(cancelFrame.Memory);
        }

        await cancellable.Cancelled.WaitAsync(TimeSpan.FromSeconds(1));

        // The cancelled request must send no response/error frame: the next frame the peer emits is
        // the response to a fresh call, proving nothing was sent for the cancelled id and the peer
        // survived.
        using (var pingFrame = CreateRequestFrame(serializer, 2, PingDispatcher.Service, "Ping"))
        {
            await clientConnection.SendAsync(pingFrame.Memory);
        }

        using var responseFrame = await clientConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(MessageFramer.TryReadFrameHeader(responseFrame.Memory, out var messageId, out var messageType));
        Assert.Equal(2, messageId);
        Assert.Equal(MessageType.Response, messageType);
    }

    [Fact]
    public async Task ConcurrentStopAsync_IsIdempotent()
    {
        var (_, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var host = RpcHost.Listen(
            new SingleConnectionServerTransport(serverConnection, ownsConnection: true),
            NewSerializer());
        await host.StartAsync();

        await Task.WhenAll(host.StopAsync(), host.StopAsync(), host.StopAsync())
            .WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static async Task<int> ReadRequestIdAsync(IRpcChannel connection)
    {
        using var requestFrame = await connection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(MessageFramer.TryReadFrameHeader(
            requestFrame.Memory,
            out var messageId,
            out var messageType));
        Assert.Equal(MessageType.Request, messageType);
        return messageId;
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest
            {
                MessageId = messageId,
                ServiceName = service,
                MethodName = method,
            },
            ReadOnlySpan<byte>.Empty);

    private static async Task SendMalformedResponseAsync(IRpcChannel connection, int messageId)
    {
        var body = new byte[MessageFramer.EnvelopeLengthSize + 1];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, MessageFramer.EnvelopeLengthSize), 1);
        body[MessageFramer.EnvelopeLengthSize] = 0xc1;

        using var responseFrame = MessageFramer.FrameToPayload(messageId, MessageType.Response, body);
        await connection.SendAsync(responseFrame.Memory);
    }

    private static async Task SendSuccessResponseAsync(
        IRpcChannel connection,
        ISerializer serializer,
        int messageId,
        int value)
    {
        using var payloadWriter = new PooledBufferWriter();
        serializer.Serialize(payloadWriter, value);
        using var responseFrame = MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Response,
            new RpcResponse { MessageId = messageId, IsSuccess = true },
            payloadWriter.WrittenMemory.Span);
        await connection.SendAsync(responseFrame.Memory);
    }

}
