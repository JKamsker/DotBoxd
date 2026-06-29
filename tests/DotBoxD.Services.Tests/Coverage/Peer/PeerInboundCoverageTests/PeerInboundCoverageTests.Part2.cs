using System.Buffers;
using System.Threading.Channels;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Peer;

public sealed partial class PeerInboundCoverageTests
{
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

    private static Payload CreateRequestFrame(
        ISerializer serializer,
        int frameMessageId,
        int envelopeMessageId,
        string service,
        string method) =>
        MessageFramer.FrameMessage(
            serializer,
            frameMessageId,
            MessageType.Request,
            new RpcRequest { MessageId = envelopeMessageId, ServiceName = service, MethodName = method },
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
