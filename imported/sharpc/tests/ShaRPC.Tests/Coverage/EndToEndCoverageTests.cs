using System.Net;
using Shared;
using ShaRPC.Core;
using ShaRPC.Core.Exceptions;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Tests;
using ShaRPC.Transports.NamedPipes;
using ShaRPC.Transports.Tcp;
using Xunit;

namespace ShaRPC.Tests.Cov.E2E;

/// <summary>
/// Full-stack end-to-end coverage that drives the generated GameService proxy + dispatcher through
/// the complete <see cref="RpcPeer"/>/<see cref="RpcHost"/> + framing/transport stack over three real
/// transports: TCP loopback, named pipes, and the in-memory pipe. These intentionally overlap the
/// existing integration suites but use distinct names and exercise additional error/cancellation/
/// concurrency/large-payload/transformer paths that the happy-path suites do not.
/// </summary>
public sealed class EndToEndCoverageTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static MessagePackRpcSerializer NewSerializer() => new();

    private static RpcPeerOptions ClientOptions() =>
        new() { RequestTimeout = TimeSpan.FromSeconds(10) };

    // ---------------------------------------------------------------------------------------------
    // Transport harness: spins up a host on a real transport, connects a client peer, and hands back
    // the generated IGameService proxy. The server peer is configured via the supplied callback so
    // each test can inject its own service instance / ExceptionTransformer.
    // ---------------------------------------------------------------------------------------------

    private sealed class TransportHarness : IAsyncDisposable
    {
        private readonly RpcHost _host;
        private readonly IAsyncDisposable _clientTransport;
        private readonly RpcPeer _client;

        public IGameService Game { get; }

        private TransportHarness(RpcHost host, IAsyncDisposable clientTransport, RpcPeer client, IGameService game)
        {
            _host = host;
            _clientTransport = clientTransport;
            _client = client;
            Game = game;
        }

        public static async Task<TransportHarness> StartTcpAsync(Action<RpcPeer> configureServer)
        {
            var serverTransport = new TcpServerTransport(IPAddress.Loopback, 0);
            var host = RpcHost
                .Listen(serverTransport, NewSerializer())
                .ForEachPeer(configureServer);
            await host.StartAsync().WaitAsync(Timeout);

            var port = serverTransport.LocalEndpoint?.Port
                ?? throw new InvalidOperationException("TCP server did not expose a bound port.");

            var clientTransport = new TcpTransport("127.0.0.1", port);
            await clientTransport.ConnectAsync().WaitAsync(Timeout);
            var client = RpcPeer.Over(clientTransport.Connection!, NewSerializer(), ClientOptions()).Start();
            return new TransportHarness(host, clientTransport, client, client.GetGameService());
        }

        public static async Task<TransportHarness> StartNamedPipeAsync(Action<RpcPeer> configureServer)
        {
            var pipeName = "sharpc-e2e-" + Guid.NewGuid().ToString("N");
            var host = RpcHost
                .Listen(new NamedPipeServerTransport(pipeName), NewSerializer())
                .ForEachPeer(configureServer);
            await host.StartAsync().WaitAsync(Timeout);

            var clientTransport = new NamedPipeClientTransport(pipeName);
            await clientTransport.ConnectAsync().WaitAsync(Timeout);
            var client = RpcPeer.Over(clientTransport.Connection!, NewSerializer(), ClientOptions()).Start();
            return new TransportHarness(host, clientTransport, client, client.GetGameService());
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            await _clientTransport.DisposeAsync();
            await _host.DisposeAsync();
        }
    }

    private static (RpcPeer Server, RpcPeer Client, IGameService Game) StartInMemoryPair(
        Action<RpcPeer> configureServer,
        int writeChunkSize = 0)
    {
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair(writeChunkSize);
        var server = RpcPeer.Over(serverConnection, NewSerializer());
        configureServer(server);
        server.Start();

        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        return (server, client, client.GetGameService());
    }

    // ---------------------------------------------------------------------------------------------
    // Happy-path round trips across all three transports — register -> get state -> move -> action.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task FullPlayerLifecycle_OverTcp_ReturnsCorrectModels()
    {
        await using var h = await TransportHarness.StartTcpAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        await RunFullLifecycleAsync(h.Game);
    }

    [Fact]
    public async Task FullPlayerLifecycle_OverNamedPipe_ReturnsCorrectModels()
    {
        await using var h = await TransportHarness.StartNamedPipeAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        await RunFullLifecycleAsync(h.Game);
    }

    [Fact]
    public async Task FullPlayerLifecycle_OverInMemoryPipe_ReturnsCorrectModels()
    {
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            await RunFullLifecycleAsync(game);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    private static async Task RunFullLifecycleAsync(IGameService game)
    {
        var registered = await game.RegisterPlayerAsync("E2E-Hero").WaitAsync(Timeout);
        Assert.Equal("E2E-Hero", registered.Name);
        Assert.Equal(1, registered.Level);
        Assert.Equal(100, registered.Health);
        Assert.NotEmpty(registered.PlayerId);

        var status = await game.GetServerStatusAsync().WaitAsync(Timeout);
        Assert.Equal("1.0.0-test", status.Version);
        Assert.Equal(1, status.PlayerCount);

        var move = await game.MovePlayerAsync(new MoveRequest
        {
            PlayerId = registered.PlayerId,
            X = 11,
            Y = 22,
            Z = 33
        }).WaitAsync(Timeout);
        Assert.True(move.Success);

        var state = await game.GetPlayerStateAsync(new PlayerId { Id = registered.PlayerId }).WaitAsync(Timeout);
        Assert.Equal(registered.PlayerId, state.PlayerId);
        Assert.Equal(11, state.PositionX);
        Assert.Equal(22, state.PositionY);
        Assert.Equal(33, state.PositionZ);

        var action = await game.PerformActionAsync(new ActionRequest
        {
            PlayerId = registered.PlayerId,
            ActionType = "Jump",
            TargetId = null
        }).WaitAsync(Timeout);
        Assert.True(action.Success);
        Assert.Contains("Jump", action.Message);
    }

    // ---------------------------------------------------------------------------------------------
    // Server-side handler throwing a typed exception: transformer OFF hides detail (InternalError),
    // transformer ON surfaces the real type and message. The TestGameService throws
    // KeyNotFoundException for unknown players, which we use as the handler exception under test.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task HandlerException_TransformerOff_SurfacesOpaqueInternalError()
    {
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            // Default: detail is hidden, opaque internal error type and message.
            Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
            Assert.Equal("Internal error.", ex.Message);
            // The real handler message must NOT leak to the caller.
            Assert.DoesNotContain("ghost", ex.Message);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task HandlerException_TransformerOn_SurfacesTypedMessageToClient()
    {
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = ex => RpcErrorInfo.FromException(ex),
        };

        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var server = RpcPeer.Over(serverConnection, NewSerializer(), serverOptions)
            .ProvideGameService(new TestGameService())
            .Start();
        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        try
        {
            var game = client.GetGameService();

            var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            // Opt-in transformer exposes the real runtime type name and message.
            Assert.Equal(nameof(KeyNotFoundException), ex.RemoteExceptionType);
            Assert.Contains("ghost", ex.Message);
            Assert.Contains("not found", ex.Message);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task HandlerException_TransformerReturningNull_FallsBackToInternalError()
    {
        // A transformer that opts a specific exception out (returns null) must produce the opaque
        // default rather than leaking detail or faulting the dispatch.
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = _ => null,
        };

        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var server = RpcPeer.Over(serverConnection, NewSerializer(), serverOptions)
            .ProvideGameService(new TestGameService())
            .Start();
        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        try
        {
            var game = client.GetGameService();

            var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            Assert.Equal(RpcErrorTypes.InternalError, ex.RemoteExceptionType);
            Assert.Equal("Internal error.", ex.Message);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task HandlerException_CustomTransformer_MapsToApplicationErrorCode()
    {
        // A tailored transformer can surface a safe, caller-facing code/message instead of the raw
        // exception — the recommended usage over FromException.
        var serverOptions = new RpcPeerOptions
        {
            ExceptionTransformer = ex => ex is KeyNotFoundException
                ? new RpcErrorInfo("No such player.", "APP_PLAYER_MISSING")
                : null,
        };

        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();
        var server = RpcPeer.Over(serverConnection, NewSerializer(), serverOptions)
            .ProvideGameService(new TestGameService())
            .Start();
        var client = RpcPeer.Over(clientConnection, NewSerializer(), ClientOptions()).Start();
        try
        {
            var game = client.GetGameService();

            var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
                () => game.GetPlayerStateAsync(new PlayerId { Id = "ghost" }).WaitAsync(Timeout));

            Assert.Equal("APP_PLAYER_MISSING", ex.RemoteExceptionType);
            Assert.Equal("No such player.", ex.Message);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Protocol-level not-found errors keep their own typed mapping (NOT routed through transformer).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task UnknownService_OverTcp_ThrowsServiceNotFound()
    {
        // Host provides nothing, so every service lookup misses.
        await using var h = await TransportHarness.StartTcpAsync(_ => { });

        var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
            () => h.Game.GetServerStatusAsync().WaitAsync(Timeout));

        Assert.Equal(RpcErrorTypes.ServiceNotFound, ex.RemoteExceptionType);
    }

    [Fact]
    public async Task UnknownMethod_OnKnownService_ThrowsMethodNotFound()
    {
        // Service is registered but we invoke a method the dispatcher does not know about, using the
        // low-level invoker on the same connection that drives the generated proxy.
        var (server, client, _) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
                () => client.InvokeAsync<ServerStatus>("IGameService", "NoSuchMethod").WaitAsync(Timeout));

            Assert.Equal(RpcErrorTypes.MethodNotFound, ex.RemoteExceptionType);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task UnknownInstance_OnKnownService_ThrowsInstanceNotFound()
    {
        // Calling a method scoped to a sub-service instance id that was never created must map to the
        // distinct InstanceNotFound error type.
        var (server, client, _) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            var ex = await Assert.ThrowsAsync<ShaRpcRemoteException>(
                () => client.InvokeOnInstanceAsync<ServerStatus>(
                    "IGameService", "no-such-instance", "GetServerStatusAsync").WaitAsync(Timeout));

            Assert.Equal(RpcErrorTypes.InstanceNotFound, ex.RemoteExceptionType);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Remote cancellation through the generated proxy: the client cancels its CancellationToken and
    // the server-side handler observes cancellation on its own token.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ClientCancellation_OverInMemoryPipe_CancelsServerSideHandler()
    {
        var service = new BlockingGameService();
        var (clientConnection, serverConnection) = InMemoryPipe.CreateConnectionPair();

        var server = RpcPeer.Over(
                serverConnection,
                NewSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .ProvideGameService(service)
            .Start();
        var client = RpcPeer.Over(
                clientConnection,
                NewSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(30) })
            .Start();

        try
        {
            var game = client.GetGameService();
            using var cts = new CancellationTokenSource();

            var call = game.GetServerStatusAsync(cts.Token);
            await service.Entered.Task.WaitAsync(Timeout);

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Timeout));
            // The server-side handler must observe cancellation on its own CancellationToken.
            await service.Canceled.Task.WaitAsync(Timeout);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Many concurrent calls on a single connection complete independently with correct results.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ManyConcurrentCalls_OverTcp_AllCompleteIndependently()
    {
        await using var h = await TransportHarness.StartTcpAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        // Register a batch of distinct players concurrently, then read each one back concurrently and
        // verify the responses are correctly correlated (no cross-talk between in-flight calls).
        var names = Enumerable.Range(0, 32).Select(i => $"P{i}").ToArray();

        var registrations = await Task.WhenAll(names.Select(n => h.Game.RegisterPlayerAsync(n)))
            .WaitAsync(Timeout);

        Assert.Equal(names.OrderBy(n => n), registrations.Select(r => r.Name).OrderBy(n => n));

        var fetches = await Task.WhenAll(
                registrations.Select(r => h.Game.GetPlayerStateAsync(new PlayerId { Id = r.PlayerId })))
            .WaitAsync(Timeout);

        var fetchedById = fetches.ToDictionary(s => s.PlayerId, s => s.Name);
        foreach (var r in registrations)
        {
            Assert.Equal(r.Name, fetchedById[r.PlayerId]);
        }
    }

    [Fact]
    public async Task ManyConcurrentStatusCalls_OverNamedPipe_AllReturnSameVersion()
    {
        await using var h = await TransportHarness.StartNamedPipeAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        var results = await Task.WhenAll(
                Enumerable.Range(0, 25).Select(_ => h.Game.GetServerStatusAsync()))
            .WaitAsync(Timeout);

        Assert.All(results, status => Assert.Equal("1.0.0-test", status.Version));
    }

    // ---------------------------------------------------------------------------------------------
    // Large payload round-trip over a real socket: a long player name forces a multi-read frame.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task LargePayload_OverTcp_RoundTripsIntact()
    {
        await using var h = await TransportHarness.StartTcpAsync(
            peer => peer.ProvideGameService(new TestGameService()));

        var bigName = new string('x', 512 * 1024);

        var registered = await h.Game.RegisterPlayerAsync(bigName).WaitAsync(Timeout);
        Assert.Equal(bigName.Length, registered.Name.Length);
        Assert.Equal(bigName, registered.Name);

        // And it survives a second hop (server-stored -> fetched back).
        var fetched = await h.Game.GetPlayerStateAsync(new PlayerId { Id = registered.PlayerId }).WaitAsync(Timeout);
        Assert.Equal(bigName, fetched.Name);
    }

    [Fact]
    public async Task LargePayload_OverByteFragmentedPipe_RoundTripsIntact()
    {
        // writeChunkSize: 1 forces every frame to be reassembled from many partial reads.
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()),
            writeChunkSize: 1);
        try
        {
            var bigName = new string('z', 64 * 1024);
            var registered = await game.RegisterPlayerAsync(bigName).WaitAsync(Timeout);
            Assert.Equal(bigName, registered.Name);
        }
        finally
        {
            await client.DisposeAsync();
            await server.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Invoking after the server disconnects throws a disconnected/connection error.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task InvokeAfterServerDisconnect_OverTcp_ThrowsConnectionError()
    {
        var serverTransport = new TcpServerTransport(IPAddress.Loopback, 0);
        var host = RpcHost
            .Listen(serverTransport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        await host.StartAsync().WaitAsync(Timeout);
        var port = serverTransport.LocalEndpoint?.Port
            ?? throw new InvalidOperationException("TCP server did not expose a bound port.");

        var clientTransport = new TcpTransport("127.0.0.1", port);
        await clientTransport.ConnectAsync().WaitAsync(Timeout);
        var client = RpcPeer.Over(clientTransport.Connection!, NewSerializer(), ClientOptions()).Start();

        try
        {
            var game = client.GetGameService();

            // Confirm the link is live before tearing the server down.
            Assert.Equal("1.0.0-test", (await game.GetServerStatusAsync().WaitAsync(Timeout)).Version);

            // Bring the whole host (and thus the accepted server peer + socket) down.
            await host.DisposeAsync().AsTask().WaitAsync(Timeout);

            // Any subsequent call must fail with a ShaRPC exception rather than hang or return.
            await Assert.ThrowsAnyAsync<ShaRpcException>(
                () => InvokeUntilFailsAsync(game).WaitAsync(Timeout));
        }
        finally
        {
            await client.DisposeAsync();
            await clientTransport.DisposeAsync();
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task InvokeAfterClientDisposed_ThrowsObjectDisposed()
    {
        var (server, client, game) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            Assert.Equal("1.0.0-test", (await game.GetServerStatusAsync().WaitAsync(Timeout)).Version);

            await client.DisposeAsync();

            // The cached proxy now points at a disposed peer: its next call must fail fast. RpcPeer
            // guards the start path with an ObjectDisposedException (see EnsureStarted) rather than
            // attempting a doomed send, so the disposed object is the surfaced fault.
            await Assert.ThrowsAnyAsync<ObjectDisposedException>(
                () => game.GetServerStatusAsync().WaitAsync(Timeout));
        }
        finally
        {
            await server.DisposeAsync();
        }
    }

    /// <summary>
    /// Drives calls until one fails. After a remote teardown the next call may briefly succeed if it
    /// was already queued; loop a bounded number of times so the test asserts the eventual failure
    /// without depending on exact timing.
    /// </summary>
    private static async Task InvokeUntilFailsAsync(IGameService game)
    {
        for (var i = 0; i < 50; i++)
        {
            await game.GetServerStatusAsync();
            await Task.Yield();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Test-only blocking game service: parks GetServerStatusAsync until cancelled so the remote
    // cancellation path can be observed end-to-end through the generated proxy.
    // ---------------------------------------------------------------------------------------------

    private sealed class BlockingGameService : IGameService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException)
            {
                Canceled.TrySetResult();
                throw;
            }

            return new ServerStatus { Version = "unreachable" };
        }

        public Task<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
