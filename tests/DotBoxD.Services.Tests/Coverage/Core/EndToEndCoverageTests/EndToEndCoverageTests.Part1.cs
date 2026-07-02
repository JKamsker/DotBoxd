using System.Net;
using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Transports.Tcp;
using Shared;
using Xunit;
namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed partial class EndToEndCoverageTests
{
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

            var ex = await Assert.ThrowsAsync<RemoteServiceException>(
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

        var ex = await Assert.ThrowsAsync<RemoteServiceException>(
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
            var ex = await Assert.ThrowsAsync<RemoteServiceException>(
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
            var ex = await Assert.ThrowsAsync<RemoteServiceException>(
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

    [Fact]
    public async Task NullInstanceId_OnInstancePrimitive_FailsBeforeSingletonDispatch()
    {
        var (server, client, _) = StartInMemoryPair(
            peer => peer.ProvideGameService(new TestGameService()));
        try
        {
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(
                () => client.InvokeOnInstanceAsync<ServerStatus>(
                    "IGameService",
                    null!,
                    "GetServerStatusAsync").WaitAsync(Timeout));

            Assert.Equal("instanceId", ex.ParamName);
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

            // Any subsequent call must fail with a DotBoxD exception rather than hang or return.
            await Assert.ThrowsAnyAsync<ServiceException>(
                () => InvokeUntilFailsAsync(game).WaitAsync(Timeout));
        }
        finally
        {
            await client.DisposeAsync();
            await clientTransport.DisposeAsync();
            await host.DisposeAsync();
        }
    }

}
