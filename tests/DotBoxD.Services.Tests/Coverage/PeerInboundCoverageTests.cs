using System.Buffers;
using System.Buffers.Binary;
using System.Threading.Channels;
using DotBoxD.Services;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Tests;
using Xunit;

namespace DotBoxD.Services.Tests.Cov.PeerInbound;

/// <summary>
/// Behavioral coverage for the inbound (server) half of <see cref="RpcPeer"/>: the frame processor's
/// header/type validation, the inbound request reader's malformed-frame branch, the dispatcher's
/// not-found / handler-throw / queue-full / cancellation paths, and teardown. All scenarios run
/// through the public <see cref="RpcPeer"/> + <see cref="RpcPeerOptions"/> surface, injecting raw
/// frames via the shared scripted connection or a real in-memory pipe link.
/// </summary>
public sealed class PeerInboundCoverageTests
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
        using var frame = MessageFramer.FrameToPayload(77, (MessageType)unknownType, ReadOnlySpan<byte>.Empty);
        connection.Enqueue(CopyFrame(frame));

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
            .Provide((IServiceDispatcher)dispatcher)
            .Start();
        peer.ProtocolError += (_, args) => protocolError.TrySetResult(args);

        await dispatcher.FirstEntered.WaitAsync(ShortTimeout);

        var args = await protocolError.Task.WaitAsync(ShortTimeout);
        Assert.Equal(7, args.MessageId);
        Assert.Equal(MessageType.Request, args.MessageType);
        Assert.Contains("Duplicate request message id", args.Message);

        dispatcher.Release();
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

    [Fact]
    public async Task Dispose_WithInFlightUnboundedDispatch_CancelsAndDrainsCleanly()
    {
        var serializer = NewSerializer();
        var connection = new ScriptedConnection();
        var dispatcher = new BlockingDispatcher();

        connection.Enqueue(CreateRequestFrame(serializer, 5, BlockingDispatcher.Service, "Hold"));

        var peer = RpcPeer
            .Over(
                connection,
                serializer,
                new RpcPeerOptions { InboundQueueCapacity = null, RequestTimeout = TimeSpan.FromMinutes(5) })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();

        await dispatcher.FirstEntered.WaitAsync(ShortTimeout);

        // The handler is parked inside DispatchAsync. Disposing cancels the linked CTS, so the
        // handler's await throws OperationCanceledException and StopAsync drains active dispatch work
        // without surfacing the cancellation.
        await peer.DisposeAsync().AsTask().WaitAsync(ShortTimeout);
        await connection.DisposeAsync();

        Assert.False(peer.IsConnected);
    }

    // ---------------- Helpers ----------------

    private static async Task<RpcResponse> ReadErrorResponseAsync(
        IRpcChannel channel, ISerializer serializer, int expectedMessageId)
    {
        using var responseFrame = await channel.ReceiveAsync().WaitAsync(ShortTimeout);
        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory, out var messageId, out var messageType, out var envelope, out _));
        Assert.Equal(expectedMessageId, messageId);
        Assert.Equal(MessageType.Error, messageType);
        return serializer.Deserialize<RpcResponse>(envelope);
    }

    private static Payload CreateRequestFrame(ISerializer serializer, int messageId, string service, string method) =>
        MessageFramer.FrameMessage(
            serializer,
            messageId,
            MessageType.Request,
            new RpcRequest { MessageId = messageId, ServiceName = service, MethodName = method },
            ReadOnlySpan<byte>.Empty);

    private static Payload RentFrame(byte[] bytes)
    {
        var payload = Payload.Rent(bytes.Length);
        bytes.CopyTo(payload.Memory.Span);
        return payload;
    }

    private static Payload CopyFrame(Payload source)
    {
        var copy = Payload.Rent(source.Length);
        source.Memory.Span.CopyTo(copy.Memory.Span);
        return copy;
    }

    private sealed class EchoDispatcher : IServiceDispatcher
    {
        public const string Service = "Echo";

        public string ServiceName => Service;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NotFoundDispatcher : IServiceDispatcher
    {
        public const string Service = "NotFoundSvc";

        public string ServiceName => Service;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new Exceptions.ServiceNotFoundException(
                $"Method '{method}' not found.",
                Exceptions.ServiceNotFoundException.NotFoundKind.Method);
    }

    private sealed class ThrowingDispatcher : IServiceDispatcher
    {
        public const string Service = "Throwing";

        private readonly string _message;

        public ThrowingDispatcher(string message) => _message = message;

        public string ServiceName => Service;

        public Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default) =>
            throw new InvalidOperationException(_message);
    }

    private sealed class CancelAwareDispatcher : IServiceDispatcher
    {
        public const string Service = "CancelAwareInbound";

        public string ServiceName => Service;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            _firstEntered.TrySetResult(true);
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult(true);
    }

    /// <summary>
    /// Marker exception thrown by <see cref="SendFailingConnection"/> so tests can assert the exact
    /// fault surfaced through the DispatchError event.
    /// </summary>
    private sealed class SendFailureException : Exception
    {
        public SendFailureException()
            : base("Send is disabled for this scripted channel.")
        {
        }
    }

    /// <summary>
    /// An <see cref="IRpcChannel"/> that delivers enqueued inbound frames but fails every send. Used to
    /// drive the dispatcher's best-effort error-send fault path and the DispatchError event without a
    /// real transport.
    /// </summary>
    private sealed class SendFailingConnection : IRpcChannel
    {
        private readonly Channel<Payload> _inbound =
            Channel.CreateUnbounded<Payload>(new UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => "send-failing://remote";

        public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
            Task.FromException(new SendFailureException());

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return default;
            }

            _inbound.Writer.TryComplete();
            while (_inbound.Reader.TryRead(out var frame))
            {
                frame.Dispose();
            }

            return default;
        }
    }
}
