using System.Buffers.Binary;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Peer;

/// <summary>
/// Behavioral coverage for the inbound (server) half of <see cref="RpcPeer"/>: the frame processor's
/// header/type validation, the inbound request reader's malformed-frame branch, the dispatcher's
/// not-found / handler-throw / queue-full / cancellation paths, and teardown. All scenarios run
/// through the public <see cref="RpcPeer"/> + <see cref="RpcPeerOptions"/> surface, injecting raw
/// frames via the shared scripted connection or a real in-memory pipe link.
/// </summary>
public sealed partial class PeerInboundCoverageTests
{
    // Hang-guard ceiling only (used for .WaitAsync(...) and as a never-expected-to-fire
    // RequestTimeout). Kept generous so 2-core CI runners under parallel load don't trip it;
    // negative timeout tests assert against their own small RequestTimeout literals elsewhere.
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);

    private static MessagePackRpcSerializer NewSerializer() => new();

    // ---- RpcPeerInboundDispatcher.AddDispatcher: duplicate service (65-66) --------------------

    [Fact]
    public async Task Provide_SameServiceTwice_Throws()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        await using var peer = RpcPeer.Over(connection, serializer);

        peer.Provide((IServiceDispatcher)new EchoDispatcher());

        var ex = Assert.Throws<InvalidOperationException>(
            () => peer.Provide((IServiceDispatcher)new EchoDispatcher()));
        Assert.Contains(EchoDispatcher.Service, ex.Message);
        Assert.Contains("already provided", ex.Message);
    }

    // ---- RpcPeerFrameProcessor: malformed frame header (25-27) --------------------------------

    [Fact]
    public async Task MalformedFrameHeader_RaisesProtocolError_AndDisposesFrame()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var protocolError = new TaskCompletionSource<RpcProtocolErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // A frame whose declared total-length prefix does not match the buffer length: TryReadFrameHeader
        // returns false, so the frame processor reports a "Malformed frame header." protocol error with
        // message id 0 and the default message type.
        var bogus = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(bogus.AsSpan(0, 4), 999); // length lies about the buffer
        connection.Enqueue(RentFrame(bogus));

        // Subscribe BEFORE Start(): the frame is already enqueued, so the read loop can process it and
        // raise ProtocolError before the handler is attached if we start first (a race that flakes on CI).
        await using var peer = RpcPeer.Over(connection, serializer);
        peer.ProtocolError += (_, args) => protocolError.TrySetResult(args);
        peer.Start();

        var args = await protocolError.Task.WaitAsync(ShortTimeout);

        Assert.Equal(0, args.MessageId);
        Assert.Contains("Malformed frame header", args.Message);
    }

    // ---- RpcPeerFrameProcessor: unknown message type (41-42) ----------------------------------

    [Fact]
    public async Task UnknownMessageType_RaisesProtocolError()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var protocolError = new TaskCompletionSource<RpcProtocolErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // A well-formed header carrying a message type outside the defined enum range hits the switch
        // default in the frame processor, which raises an "Unknown message type." protocol error.
        const byte unknownType = 0x7F;
        var frame = new byte[MessageFramer.HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), 77);
        frame[8] = unknownType;
        connection.Enqueue(RentFrame(frame));

        // Subscribe BEFORE Start(): the frame is already enqueued, so the read loop can process it and
        // raise ProtocolError before the handler is attached if we start first (a race that flakes on CI).
        await using var peer = RpcPeer.Over(connection, serializer);
        peer.ProtocolError += (_, args) => protocolError.TrySetResult(args);
        peer.Start();

        var args = await protocolError.Task.WaitAsync(ShortTimeout);

        Assert.Equal(77, args.MessageId);
        Assert.Equal((MessageType)unknownType, args.MessageType);
        Assert.Contains("Unknown message type", args.Message);
    }

    // ---- RpcPeerInboundRequestReader: malformed request frame (23-25) -------------------------

    [Fact]
    public async Task RequestFrame_WithoutEnvelope_RaisesMalformedRequestFrameError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        var protocolError = new TaskCompletionSource<RpcProtocolErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var peer = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = ShortTimeout })
            .Start();
        peer.ProtocolError += (_, args) => protocolError.TrySetResult(args);

        // A Request frame with only the 9-byte header (no 4-byte envelope-length prefix) passes the
        // header check but fails MessageFramer.TryReadFrame inside the reader, which reports
        // "Malformed request frame." (distinct from the "Malformed request envelope." deserialize path).
        using var headerOnly = MessageFramer.FrameToPayload(55, MessageType.Request, ReadOnlySpan<byte>.Empty);
        await client.SendAsync(headerOnly.Memory);

        using var responseFrame = await client.ReceiveAsync().WaitAsync(ShortTimeout);
        var args = await protocolError.Task.WaitAsync(ShortTimeout);

        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory, out var messageId, out var messageType, out var envelope, out _));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.Equal(55, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.Equal(55, args.MessageId);
        Assert.Equal(MessageType.Request, args.MessageType);
        Assert.Contains("Malformed request frame", args.Message);
        Assert.Equal(RpcErrorTypes.ProtocolError, response.ErrorType);
    }

    // ---- RpcPeerInboundDispatcher: duplicate inbound message id (184-187) ----------------------

    [Fact]
    public async Task DuplicateInboundMessageId_RaisesProtocolError()
    {
        var serializer = NewSerializer();
        await using var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();
        var protocolError = new TaskCompletionSource<RpcProtocolErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Two Request frames with the SAME message id. The first holds a dispatch slot (it is still
        // active in _activeInbound); the second collides on the _activeInbound.TryAdd, so
        // TryCreateInboundRequest reports "Duplicate request message id." and the dispatcher answers it
        // with a protocol error frame instead of dispatching it twice.
        connection.Enqueue(CreateRequestFrame(serializer, 7, BlockingDispatcher.Service, "Hold"));
        connection.Enqueue(CreateRequestFrame(serializer, 7, BlockingDispatcher.Service, "Hold"));

        await using var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions
                {
                    InboundQueueCapacity = null, // immediate inline dispatch keeps id 7 active
                    RequestTimeout = ShortTimeout,
                })
            .Provide((IServiceDispatcher)dispatcher);
        peer.ProtocolError += (_, args) => protocolError.TrySetResult(args);
        peer.Start();

        try
        {
            await dispatcher.FirstEntered.WaitAsync(ShortTimeout);

            var args = await protocolError.Task.WaitAsync(ShortTimeout);
            Assert.Equal(7, args.MessageId);
            Assert.Equal(MessageType.Request, args.MessageType);
            Assert.Contains("Duplicate request message id", args.Message);
        }
        finally
        {
            dispatcher.Release();
        }
    }

    // ---- RpcDispatchResponseBuilder: unknown service -> ServiceNotFound -----------------------

    [Fact]
    public async Task UnknownService_ReturnsServiceNotFoundError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;

        await using var peer = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = ShortTimeout })
            .Start();

        using var requestFrame = CreateRequestFrame(serializer, 11, "DoesNotExist", "Anything");
        await client.SendAsync(requestFrame.Memory);

        var response = await ReadErrorResponseAsync(client, serializer, expectedMessageId: 11);
        Assert.Equal(RpcErrorTypes.ServiceNotFound, response.ErrorType);
        Assert.False(response.IsSuccess);
    }

    // ---- Dispatcher throws ServiceNotFoundException(Method) -> MethodNotFound -------------------

    [Fact]
    public async Task HandlerThrowsMethodNotFound_ReturnsMethodNotFoundError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;

        await using var peer = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = ShortTimeout })
            .Provide((IServiceDispatcher)new NotFoundDispatcher())
            .Start();

        using var requestFrame = CreateRequestFrame(serializer, 21, NotFoundDispatcher.Service, "Missing");
        await client.SendAsync(requestFrame.Memory);

        var response = await ReadErrorResponseAsync(client, serializer, expectedMessageId: 21);
        Assert.Equal(RpcErrorTypes.MethodNotFound, response.ErrorType);
        Assert.Contains("Missing", response.ErrorMessage);
    }

    // ---- Handler throws generic exception -> opaque InternalError (default transformer) --------

    [Fact]
    public async Task HandlerThrows_WithoutTransformer_ReturnsOpaqueInternalError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;

        await using var peer = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = ShortTimeout })
            .Provide((IServiceDispatcher)new ThrowingDispatcher("super-secret-internal-detail"))
            .Start();

        using var requestFrame = CreateRequestFrame(serializer, 31, ThrowingDispatcher.Service, "Boom");
        await client.SendAsync(requestFrame.Memory);

        var response = await ReadErrorResponseAsync(client, serializer, expectedMessageId: 31);
        Assert.Equal(RpcErrorTypes.InternalError, response.ErrorType);
        Assert.Equal("Internal error.", response.ErrorMessage);
        Assert.DoesNotContain("super-secret-internal-detail", response.ErrorMessage ?? string.Empty);
    }

    // ---- Handler throws + ExceptionTransformer surfaces detail --------------------------------

}
