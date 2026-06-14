using ShaRPC.Core;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using Shared;
using Xunit;

namespace ShaRPC.Tests.Cov.Host;

/// <summary>
/// Behavioral coverage for <see cref="RpcHost"/> and its internal accept loop / peer collection.
/// Everything is driven through the public surface (RpcHost, the public
/// SingleConnectionServerTransport, RpcPeer over InMemoryPipe connections) so the internal
/// RpcHostAcceptLoop, RpcHostPeerCollection and RpcHostPeerConfiguration types are exercised only
/// as a consequence of real start/accept/disconnect/dispose scenarios.
/// </summary>
public sealed class HostCoverageTests
{
    private static readonly TimeSpan Timeout5s = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Timeout10s = TimeSpan.FromSeconds(10);

    private static MessagePackRpcSerializer NewSerializer() => new();

    private static RpcPeerOptions ClientOptions() =>
        new() { RequestTimeout = Timeout5s };

    // ----- Listen argument validation (RpcHost lines 39-40, 44-45) -----

    [Fact]
    public void Listen_NullListener_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcHost.Listen(null!, NewSerializer()));
        Assert.Equal("listener", ex.ParamName);
    }

    [Fact]
    public void Listen_NullSerializer_ThrowsArgumentNullException()
    {
        var connection = new ScriptedConnection();
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcHost.Listen(new SingleConnectionServerTransport(connection), null!));
        Assert.Equal("serializer", ex.ParamName);
    }

    [Fact]
    public void ForEachPeer_NullConfigure_ThrowsArgumentNullException()
    {
        var connection = new ScriptedConnection();
        var host = RpcHost.Listen(new SingleConnectionServerTransport(connection), NewSerializer());
        Assert.Throws<ArgumentNullException>(() => host.ForEachPeer(null!));
    }

    // ----- StartAsync lifecycle guards (RpcHost 80-81, 84-86, 125-128, 134-140, 167-169, 174) -----

    [Fact]
    public async Task StartAsync_CalledTwice_ThrowsInvalidOperationException()
    {
        var connection = new ScriptedConnection();
        await using var host = RpcHost.Listen(
            new SingleConnectionServerTransport(connection), NewSerializer());

        await host.StartAsync().WaitAsync(Timeout5s);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
        Assert.Contains("already running", ex.Message);
    }

    [Fact]
    public async Task StartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var connection = new ScriptedConnection();
        var host = RpcHost.Listen(
            new SingleConnectionServerTransport(connection), NewSerializer());

        await host.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
    }

    [Fact]
    public async Task StartAsync_WhenListenerStartFails_ResetsStateAndRethrows()
    {
        var failure = new InvalidOperationException("bind failed");
        var transport = new FailingStartServerTransport(failure);
        await using var host = RpcHost.Listen(transport, NewSerializer());

        // First start surfaces the listener failure (RpcHost 98-110, 112).
        var first = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
        Assert.Same(failure, first);

        // The failed start must have reset internal state so a retry is allowed (not "already
        // running"). The second attempt fails the same way, proving the reset happened.
        var second = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.StartAsync().WaitAsync(Timeout5s));
        Assert.Same(failure, second);
        Assert.Equal(2, transport.StartCalls);
    }

    [Fact]
    public async Task StopAsync_BeforeStart_IsNoOp()
    {
        var connection = new ScriptedConnection();
        await using var host = RpcHost.Listen(
            new SingleConnectionServerTransport(connection), NewSerializer());

        // _cts is null -> StopAsync returns a completed task without faulting (RpcHost 181-183).
        await host.StopAsync().WaitAsync(Timeout5s);
    }

    // ----- Accept -> peer creation, PeerConnected, collection.Add -----

    [Fact]
    public async Task StartAsync_AcceptsConnection_CreatesPeerAndRaisesPeerConnected()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        var acceptedPeer = await connected.Task.WaitAsync(Timeout5s);
        Assert.True(acceptedPeer.IsConnected);

        // Drive a real call through the accepted peer's read loop so the peer is genuinely live.
        var service = client.GetGameService();
        var registered = await service.RegisterPlayerAsync("p1").WaitAsync(Timeout5s);
        Assert.Equal("p1", registered.Name);
    }

    [Fact]
    public async Task MultiplePeers_AreTrackedConcurrently_AndAllDisposedOnHostStop()
    {
        const int peerCount = 4;
        var transport = new MultiConnectionServerTransport();
        var connectedPeers = new System.Collections.Concurrent.ConcurrentBag<RpcPeer>();
        var allConnected = new CountdownEvent(peerCount);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) =>
        {
            connectedPeers.Add(args.Peer);
            allConnected.Signal();
        };
        await host.StartAsync().WaitAsync(Timeout5s);

        var clients = new List<RpcPeer>();
        try
        {
            for (var i = 0; i < peerCount; i++)
            {
                var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
                clients.Add(RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start());
                transport.EnqueueConnection(serverConnection);
            }

            Assert.True(allConnected.Wait(Timeout10s));
            Assert.Equal(peerCount, connectedPeers.Count);
            Assert.All(connectedPeers, peer => Assert.True(peer.IsConnected));

            // Stopping the host must close every tracked peer (RpcHostPeerCollection.CloseAllAsync
            // 41-45, AwaitCleanupAsync).
            await host.StopAsync().WaitAsync(Timeout10s);
            Assert.All(connectedPeers, peer => Assert.False(peer.IsConnected));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
        allConnected.Dispose();
    }

    // ----- Peer disconnect removes from collection + raises PeerDisconnected -----

    [Fact]
    public async Task PeerDisconnect_RaisesPeerDisconnected_AndRemovesFromCollection()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        host.PeerDisconnected += (_, args) => disconnected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        var acceptedPeer = await connected.Task.WaitAsync(Timeout5s);

        // Closing the client tears down the channel; the accepted peer's read loop ends and the host's
        // OnPeerDisconnected runs: removes the peer, raises PeerDisconnected, DisposeInBackground
        // (RpcHost 302-316).
        await client.DisposeAsync();

        var disconnectedPeer = await disconnected.Task.WaitAsync(Timeout10s);
        Assert.Same(acceptedPeer, disconnectedPeer);

        // After disconnect the host should still stop cleanly (peer already removed from collection).
        await host.StopAsync().WaitAsync(Timeout10s);
    }

    // ----- Configuration failure path (RpcHost 264-269, RpcHostPeerConfiguration.Snapshot) -----

    [Fact]
    public async Task ForEachPeer_ConfigurationThrows_RaisesAcceptError_AndDoesNotRaisePeerConnected()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var failure = new InvalidOperationException("configure boom");
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var peerConnectedCount = 0;

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(_ => throw failure);
        host.AcceptError += (_, args) => error.TrySetResult(args.Error);
        host.PeerConnected += (_, _) => Interlocked.Increment(ref peerConnectedCount);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        Assert.Same(failure, await error.Task.WaitAsync(Timeout10s));
        Assert.Equal(0, Volatile.Read(ref peerConnectedCount));
    }

    [Fact]
    public async Task ForEachPeer_MultipleConfigurators_AllRunInRegistrationOrder()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var order = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverConnection), NewSerializer())
            .ForEachPeer(_ => order.Enqueue(1))
            .ForEachPeer(_ => order.Enqueue(2))
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, _) => connected.TrySetResult(true);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        Assert.True(await connected.Task.WaitAsync(Timeout10s));
        Assert.Equal(new[] { 1, 2 }, order.ToArray());
    }

    // ----- Accept loop error handling (RpcHostAcceptLoop 38-46, 107-117) -----

    [Fact]
    public async Task AcceptAsync_TransientErrors_RaiseAcceptError_AndLoopKeepsAccepting()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var faultCount = 2;
        var transport = new FaultThenAcceptServerTransport(faultCount, serverConnection);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var errorsSeen = new CountdownEvent(faultCount);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.AcceptError += (_, args) =>
        {
            errors.Add(args.Error);
            if (errorsSeen.CurrentCount > 0)
            {
                errorsSeen.Signal();
            }
        };
        host.PeerConnected += (_, _) => connected.TrySetResult(true);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        // The loop reported each transient accept failure as an AcceptError ...
        Assert.True(errorsSeen.Wait(Timeout10s));
        // ... and after backing off kept looping, eventually accepting the real connection.
        Assert.True(await connected.Task.WaitAsync(Timeout10s));
        Assert.All(errors, ex => Assert.IsType<InvalidOperationException>(ex));
        errorsSeen.Dispose();
    }

    [Fact]
    public async Task RaiseAcceptError_WithNoHandler_DoesNotFaultTheLoop()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var transport = new FaultThenAcceptServerTransport(faultCount: 1, serverConnection);
        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // No AcceptError subscriber: the loop must still continue past the fault and accept.
        await using var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, _) => connected.TrySetResult(true);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();

        Assert.True(await connected.Task.WaitAsync(Timeout10s));
    }

    // ----- DisposeAsync stops the accept loop and disposes peers -----

    [Fact]
    public async Task DisposeAsync_StopsAcceptLoop_DisposesPeers_AndDisposesListener()
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var transport = new DisposeTrackingSingleConnectionTransport(serverConnection);
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        var host = RpcHost
            .Listen(transport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => connected.TrySetResult(args.Peer);
        await host.StartAsync().WaitAsync(Timeout5s);

        await using var client = RpcPeer
            .Over(clientConnection, NewSerializer(), ClientOptions())
            .Start();
        var acceptedPeer = await connected.Task.WaitAsync(Timeout5s);
        Assert.True(acceptedPeer.IsConnected);

        await host.DisposeAsync().AsTask().WaitAsync(Timeout10s);

        Assert.False(acceptedPeer.IsConnected);
        Assert.True(transport.IsDisposed);

        // DisposeAsync is idempotent: a second call returns without faulting (RpcHost 320-322).
        await host.DisposeAsync().AsTask().WaitAsync(Timeout5s);
    }

    [Fact]
    public async Task DisposeAsync_OnNeverStartedHost_DisposesListenerWithoutFault()
    {
        var connection = new ScriptedConnection();
        var transport = new DisposeTrackingSingleConnectionTransport(connection);
        var host = RpcHost.Listen(transport, NewSerializer());

        await host.DisposeAsync().AsTask().WaitAsync(Timeout5s);

        Assert.True(transport.IsDisposed);
    }

    // ----------------------------------------------------------------------------------------
    // Test transports
    // ----------------------------------------------------------------------------------------

    /// <summary>Server transport whose <c>StartAsync</c> always throws, to drive the host's
    /// listener-start failure path.</summary>
    private sealed class FailingStartServerTransport : IServerTransport
    {
        private readonly Exception _failure;
        private int _startCalls;

        public FailingStartServerTransport(Exception failure) => _failure = failure;

        public int StartCalls => Volatile.Read(ref _startCalls);

        public Task StartAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _startCalls);
            throw _failure;
        }

        public Task<IRpcChannel> AcceptAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Accept should never run when start fails.");

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Server transport that hands back queued connections one per accept, then parks until
    /// stopped/cancelled — lets the host accept several peers concurrently.</summary>
    private sealed class MultiConnectionServerTransport : IServerTransport
    {
        private readonly System.Threading.Channels.Channel<IRpcChannel> _connections =
            System.Threading.Channels.Channel.CreateUnbounded<IRpcChannel>(
                new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
        private int _disposed;

        public void EnqueueConnection(IRpcChannel connection) => _connections.Writer.TryWrite(connection);

        public Task StartAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(MultiConnectionServerTransport));
            }

            return Task.CompletedTask;
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            try
            {
                return await _connections.Reader.ReadAsync(ct).ConfigureAwait(false);
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
                throw new OperationCanceledException(ct);
            }
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _connections.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _connections.Writer.TryComplete();
            return default;
        }
    }

    /// <summary>Accept fails the first <c>faultCount</c> times (driving the loop's error/backoff
    /// path), then returns one real connection, then parks until cancelled.</summary>
    private sealed class FaultThenAcceptServerTransport : IServerTransport
    {
        private readonly int _faultCount;
        private readonly IRpcChannel _connection;
        private int _acceptCalls;
        private int _delivered;

        public FaultThenAcceptServerTransport(int faultCount, IRpcChannel connection)
        {
            _faultCount = faultCount;
            _connection = connection;
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _acceptCalls);
            if (call <= _faultCount)
            {
                throw new InvalidOperationException($"transient accept failure #{call}");
            }

            if (Interlocked.Exchange(ref _delivered, 1) == 0)
            {
                return _connection;
            }

            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
            throw new OperationCanceledException(ct);
        }

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => default;
    }

    /// <summary>Single-connection server transport that records whether the host disposed it.</summary>
    private sealed class DisposeTrackingSingleConnectionTransport : IServerTransport
    {
        private readonly IRpcChannel _connection;
        private readonly TaskCompletionSource<bool> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _accepted;
        private int _started;
        private int _disposed;

        public DisposeTrackingSingleConnectionTransport(IRpcChannel connection) => _connection = connection;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public Task StartAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref _started, 1);
            return Task.CompletedTask;
        }

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _started) == 0)
            {
                throw new InvalidOperationException("Transport not started.");
            }

            if (Interlocked.Exchange(ref _accepted, 1) == 0)
            {
                return _connection;
            }

            using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), _stopped))
            {
                await _stopped.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            throw new OperationCanceledException();
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            _stopped.TrySetResult(true);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _stopped.TrySetResult(true);
            return default;
        }
    }
}
