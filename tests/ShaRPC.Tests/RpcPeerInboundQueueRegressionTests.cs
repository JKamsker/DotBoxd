using System.Buffers;
using System.Buffers.Binary;
using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;
using ShaRPC.Core.Server;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using Shared;
using Xunit;

namespace ShaRPC.Tests;

public sealed class RpcPeerInboundQueueRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task WaitQueue_ReadsReentrantResponse_WhenRequestQueueIsFull()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var blocking = new BlockingDispatcher();

        await using var server = RpcPeer.Over(
            serverConnection,
            serializer,
            new RpcPeerOptions
            {
                InboundQueueCapacity = 1,
                QueueFullMode = ShaRpcQueueFullMode.Wait,
                RequestTimeout = TimeSpan.FromMilliseconds(750),
            });
        server
            .Provide((IServiceDispatcher)new ReentrantCallbackDispatcher(
                () => server.GetPlayerNotifications()))
            .Provide((IServiceDispatcher)blocking)
            .Start();

        var notifications = new QueueFillingNotifications(clientConnection, serializer);
        await using var client = RpcPeer
            .Over(clientConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Provide<IPlayerNotifications>(notifications)
            .Start();

        var call = client.InvokeAsync<string>(ReentrantCallbackDispatcher.Service, "Call");
        try
        {
            await notifications.BackpressureFramesSent.WaitAsync(TimeSpan.FromSeconds(1));
            var result = await call.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal("callback-ok", result);
        }
        finally
        {
            blocking.Release();
        }
    }

    [Fact]
    public async Task MalformedInboundRequest_RaisesProtocolError()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var protocolError = new TaskCompletionSource<RpcProtocolErrorEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();
        server.ProtocolError += (_, args) => protocolError.TrySetResult(args);

        using var requestFrame = CreateMalformedRequestFrame(42);
        await clientConnection.SendAsync(requestFrame.Memory);

        using var responseFrame = await clientConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));
        var args = await protocolError.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory,
            out var messageId,
            out var messageType,
            out _,
            out _));
        Assert.Equal(42, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.Equal(42, args.MessageId);
        Assert.Equal(MessageType.Request, args.MessageType);
        Assert.Contains("Malformed request envelope", args.Message);
    }

    private static Payload CreateMalformedRequestFrame(int messageId)
    {
        var body = new byte[MessageFramer.EnvelopeLengthSize + 1];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, MessageFramer.EnvelopeLengthSize), 1);
        body[MessageFramer.EnvelopeLengthSize] = 0xc1;
        return MessageFramer.FrameToPayload(messageId, MessageType.Request, body);
    }

    private sealed class ReentrantCallbackDispatcher : IServiceDispatcher
    {
        public const string Service = "ReentrantCallback";

        private readonly Func<IPlayerNotifications> _getNotifications;

        public ReentrantCallbackDispatcher(Func<IPlayerNotifications> getNotifications) =>
            _getNotifications = getNotifications;

        public string ServiceName => Service;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            if (method != "Call")
            {
                throw new ShaRpcNotFoundException($"Method '{method}' not found.");
            }

            await _getNotifications().WhoAmIAsync(ct).ConfigureAwait(false);
            serializer.Serialize(output, "callback-ok");
        }
    }

    private sealed class QueueFillingNotifications : IPlayerNotifications
    {
        private readonly IConnection _connection;
        private readonly ISerializer _serializer;
        private readonly TaskCompletionSource<bool> _sent =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _messageId = 10_000;

        public QueueFillingNotifications(IConnection connection, ISerializer serializer)
        {
            _connection = connection;
            _serializer = serializer;
        }

        public Task BackpressureFramesSent => _sent.Task;

        public Task NotifyAsync(string message, CancellationToken ct = default) => Task.CompletedTask;

        public async Task<string> WhoAmIAsync(CancellationToken ct = default)
        {
            await SendBlockingRequestAsync(ct).ConfigureAwait(false);
            await SendBlockingRequestAsync(ct).ConfigureAwait(false);
            _sent.TrySetResult(true);
            return "queue-filler";
        }

        private async Task SendBlockingRequestAsync(CancellationToken ct)
        {
            var messageId = Interlocked.Increment(ref _messageId);
            using var frame = MessageFramer.FrameMessage(
                _serializer,
                messageId,
                MessageType.Request,
                new RpcRequest
                {
                    MessageId = messageId,
                    ServiceName = BlockingDispatcher.Service,
                    MethodName = "Hold",
                },
                ReadOnlySpan<byte>.Empty);
            await _connection.SendAsync(frame.Memory, ct).ConfigureAwait(false);
        }
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Blocking";

        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ServiceName => Service;

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            await _release.Task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Release() => _release.TrySetResult(true);
    }
}
