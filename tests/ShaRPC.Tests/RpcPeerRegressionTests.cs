using System.Buffers;
using System.Buffers.Binary;
using ShaRPC.Core;
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

public sealed class RpcPeerRegressionTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

    [Fact]
    public async Task PendingCall_FailsWithConnectionException_WhenRemoteClosesCleanly()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Start();

        var call = client.InvokeAsync<int>("MissingService", "NeverCompletes");

        using (await serverConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1)))
        {
        }

        await serverConnection.DisposeAsync();

        await Assert.ThrowsAsync<ShaRpcConnectionException>(
            () => call.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task StopAsync_ClosesAcceptedPeers()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(peer => peer.Provide<IGameService>(new TestGameService()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync();

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();
        var game = client.GetGameService();

        Assert.NotNull(await game.GetServerStatusAsync());
        var acceptedPeer = await connected.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await host.StopAsync();

        Assert.False(acceptedPeer.IsConnected);
        await Assert.ThrowsAsync<ShaRpcConnectionException>(
            () => game.GetServerStatusAsync().WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task WaitQueue_DispatchesSeriallyAndAppliesBackpressure()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var dispatcher = new BlockingDispatcher();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.Wait,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var calls = Enumerable.Range(0, 3)
            .Select(_ => client.InvokeAsync(BlockingDispatcher.Service, "Hold"))
            .ToArray();

        try
        {
            await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));

            // The single dispatch worker is blocked inside the first handler, so the second request
            // cannot have entered dispatch yet. Assert that deterministically instead of sampling
            // after a fixed delay.
            Assert.False(dispatcher.SecondEntered.IsCompleted);
            Assert.Equal(1, dispatcher.MaxActive);
        }
        finally
        {
            dispatcher.Release();
        }

        await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, dispatcher.MaxActive);
    }

    [Fact]
    public async Task InboundQueueCapacity_DropIncoming_DropsFramesBeyondBoundedQueue()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var dispatcher = new BlockingDispatcher();

        await using var server = RpcPeer
            .Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions
                {
                    InboundQueueCapacity = 1,
                    QueueFullMode = ShaRpcQueueFullMode.DropIncoming,
                    RequestTimeout = TimeSpan.FromSeconds(5),
                })
            .Provide((IServiceDispatcher)dispatcher)
            .Start();
        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromMilliseconds(750) })
            .Start();

        var calls = Enumerable.Range(0, 3)
            .Select(_ => client.InvokeAsync(BlockingDispatcher.Service, "Hold"))
            .ToArray();

        await dispatcher.FirstEntered.WaitAsync(TimeSpan.FromSeconds(1));

        // The dropped request(s) now come back as an explicit queue-full RPC error instead of the
        // client waiting out its RequestTimeout; the dispatched + queued requests succeed once the
        // handler is released here. The InRange tolerances absorb whether one or two frames were
        // dropped (which depends on dispatch-worker timing).
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        dispatcher.Release();

        var outcomes = await Task.WhenAll(calls.Select(CaptureExceptionAsync))
            .WaitAsync(TimeSpan.FromSeconds(2));

        var successes = outcomes.Count(static exception => exception is null);
        var dropped = outcomes
            .OfType<ShaRpcRemoteException>()
            .Count(static ex => ex.RemoteExceptionType == RpcErrorTypes.QueueFull);

        Assert.Equal(1, dispatcher.MaxActive);
        Assert.InRange(successes, 1, 2);
        Assert.InRange(dropped, 1, 2);
        Assert.Equal(3, successes + dropped);
    }

    [Fact]
    public async Task UnknownMethod_OnProvidedService_AnsweredWithMethodNotFound()
    {
        var serializer = NewSerializer();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        await using var client = clientConnection;
        await using var peer = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Provide<IGameService>(new TestGameService())
            .Start();

        var wireName = ShaRPC.Core.Generated.ShaRpcServiceRegistry.GetService(typeof(IGameService)).ServiceName;

        using var requestFrame = MessageFramer.FrameMessage(
            serializer,
            7,
            MessageType.Request,
            new RpcRequest { MessageId = 7, ServiceName = wireName, MethodName = "NoSuchMethod" },
            ReadOnlySpan<byte>.Empty);
        await client.SendAsync(requestFrame.Memory);

        using var responseFrame = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(MessageFramer.TryReadFrame(responseFrame.Memory, out _, out var messageType, out var envelope, out _));
        var response = serializer.Deserialize<RpcResponse>(envelope);

        // A missing method on a known service maps to MethodNotFound — distinct from the
        // ServiceNotFound a missing service produces, so callers can tell the two apart.
        Assert.Equal(MessageType.Error, messageType);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.MethodNotFound, response.ErrorType);
    }

    [Fact]
    public async Task MalformedInboundRequest_ReturnsProtocolErrorResponse()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var serializer = NewSerializer();

        await using var server = RpcPeer
            .Over(serverConnection, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        var body = new byte[MessageFramer.EnvelopeLengthSize + 1];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(0, MessageFramer.EnvelopeLengthSize), 1);
        body[MessageFramer.EnvelopeLengthSize] = 0xc1;

        using var requestFrame = MessageFramer.FrameToPayload(42, MessageType.Request, body);
        await clientConnection.SendAsync(requestFrame.Memory);

        using var responseFrame = await clientConnection.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(MessageFramer.TryReadFrame(
            responseFrame.Memory,
            out var messageId,
            out var messageType,
            out var envelope,
            out var payload));

        var response = serializer.Deserialize<RpcResponse>(envelope);

        Assert.Equal(42, messageId);
        Assert.Equal(MessageType.Error, messageType);
        Assert.Equal(0, payload.Length);
        Assert.False(response.IsSuccess);
        Assert.Equal(RpcErrorTypes.ProtocolError, response.ErrorType);
        Assert.Contains("Malformed request envelope", response.ErrorMessage);
    }

    [Fact]
    public async Task DisconnectedHandlers_AreIsolated_WhenOneHandlerThrows()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var firstHandlerCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondHandlerCalled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var server = RpcPeer
            .Over(serverConnection, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .Start();

        server.Disconnected += (_, _) =>
        {
            firstHandlerCalled.TrySetResult(true);
            throw new InvalidOperationException("Handler failure.");
        };
        server.Disconnected += (_, _) => secondHandlerCalled.TrySetResult(true);

        await clientConnection.DisposeAsync();

        await firstHandlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await secondHandlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static async Task<Exception?> CaptureExceptionAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class BlockingDispatcher : IServiceDispatcher
    {
        public const string Service = "Blocking";

        private readonly TaskCompletionSource<bool> _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maxActive;
        private int _started;

        public string ServiceName => Service;

        public Task FirstEntered => _firstEntered.Task;

        public Task SecondEntered => _secondEntered.Task;

        public int MaxActive => Volatile.Read(ref _maxActive);

        public async Task DispatchAsync(
            string method,
            ReadOnlyMemory<byte> payload,
            ISerializer serializer,
            IInstanceRegistry registry,
            IBufferWriter<byte> output,
            CancellationToken ct = default)
        {
            var started = Interlocked.Increment(ref _started);
            if (started == 1)
            {
                _firstEntered.TrySetResult(true);
            }
            else if (started == 2)
            {
                _secondEntered.TrySetResult(true);
            }

            var active = Interlocked.Increment(ref _active);
            RecordMaxActive(active);
            try
            {
                await _release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Release() => _release.TrySetResult(true);

        private void RecordMaxActive(int active)
        {
            while (true)
            {
                var maxActive = Volatile.Read(ref _maxActive);
                if (active <= maxActive)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxActive, active, maxActive) == maxActive)
                {
                    return;
                }
            }
        }
    }

}
