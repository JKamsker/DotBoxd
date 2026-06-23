using System.Collections.Concurrent;
using DotBoxD.Services.Exceptions;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Peer.PeerTestSupport;

namespace DotBoxD.Services.Tests.Peer;

/// <summary>
/// End-to-end coverage for the symmetric <see cref="RpcPeer"/> model over an in-memory
/// duplex channel: one-directional calls, bidirectional calls on a single connection, and
/// the explicit reject-inbound behaviour.
/// </summary>
public sealed class PeerTests
{
    [Fact]
    public async Task OneDirectional_GetOnlyPeer_CallsProviderAndGetsData()
    {
        var (clientChannel, serverChannel) = InMemoryChannel.CreatePair();

        await using var provider = RpcPeer.Over(serverChannel, NewSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();

        await using var caller = RpcPeer.Over(clientChannel, NewSerializer(),
                new RpcPeerOptions { RejectInboundCalls = true })
            .Start();

        var game = caller.GetGameService();
        var status = await game.GetServerStatusAsync();

        Assert.NotNull(status);

        var player = await game.RegisterPlayerAsync("Peer");
        Assert.Equal("Peer", player.Name);
    }

    [Fact]
    public async Task Get_AfterDispose_ThrowsObjectDisposed()
    {
        var (clientChannel, _) = InMemoryChannel.CreatePair();
        var caller = RpcPeer.Over(clientChannel, NewSerializer(),
            new RpcPeerOptions { RejectInboundCalls = true });

        await caller.DisposeAsync();

        // Get must fail fast on a disposed peer rather than handing back a proxy that only throws on
        // its first call.
        Assert.Throws<ObjectDisposedException>(() => caller.Get<IGameService>());
    }

    [Fact]
    public async Task Bidirectional_BothSidesProvideAndCall_OverOneConnection()
    {
        var (channelA, channelB) = InMemoryChannel.CreatePair();

        // Side A provides the game; side B provides player notifications.
        await using var a = RpcPeer.Over(channelA, NewSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();

        var notifications = new RecordingNotifications("client-42");
        await using var b = RpcPeer.Over(channelB, NewSerializer())
            .Provide<IPlayerNotifications>(notifications)
            .Start();

        // B -> A : call the game service.
        var game = b.GetGameService();
        var registered = await game.RegisterPlayerAsync("Hero");
        Assert.Equal("Hero", registered.Name);

        // A -> B : call back into the connecting peer over the SAME connection.
        var callback = a.GetPlayerNotifications();
        await callback.NotifyAsync("level-up");
        var who = await callback.WhoAmIAsync();

        Assert.Equal("client-42", who);
        Assert.Equal("level-up", Assert.Single(notifications.Messages));
    }

    [Fact]
    public async Task RejectInboundCalls_ProducesRemoteError_WhenOtherSideCallsBack()
    {
        var (channelA, channelB) = InMemoryChannel.CreatePair();

        // A provides the game AND tries to call back into B.
        await using var a = RpcPeer.Over(channelA, NewSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();

        // B refuses inbound calls but still calls out.
        await using var b = RpcPeer.Over(channelB, NewSerializer(),
                new RpcPeerOptions { RejectInboundCalls = true, RequestTimeout = TimeSpan.FromSeconds(5) })
            .Provide<IPlayerNotifications>(new RecordingNotifications("nope"))
            .Start();

        // Outbound call from B still works.
        var game = b.GetGameService();
        Assert.NotNull(await game.GetServerStatusAsync());

        // Inbound call from A is rejected by B.
        var callback = a.GetPlayerNotifications();
        var ex = await Assert.ThrowsAsync<RemoteServiceException>(() => callback.WhoAmIAsync());
        Assert.Contains("does not accept inbound calls", ex.Message);
    }

    [Fact]
    public async Task RpcHost_AcceptsConnections_AndCallsBackIntoConnectingPeer()
    {
        var (clientChannel, serverChannel) = InMemoryChannel.CreatePair();

        RpcPeer? hostPeer = null;
        var connected = new TaskCompletionSource<RpcPeer>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverChannel), NewSerializer())
            .ForEachPeer(peer => peer.Provide<IGameService>(new TestGameService()));
        host.PeerConnected += (_, args) =>
        {
            hostPeer = args.Peer;
            connected.TrySetResult(args.Peer);
        };
        await host.StartAsync();

        var notifications = new RecordingNotifications("host-client");
        await using var client = RpcPeer.Over(clientChannel, NewSerializer())
            .Provide<IPlayerNotifications>(notifications)
            .Start();

        var game = client.GetGameService();
        Assert.NotNull(await game.GetServerStatusAsync());

        var accepted = await connected.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var callback = accepted.GetPlayerNotifications();
        await callback.NotifyAsync("hello from host");

        Assert.Equal("hello from host", Assert.Single(notifications.Messages));
    }

    [Fact]
    public async Task RpcHost_AcceptsMultiplePeers_CallsEach_AndClosesAllOnStop()
    {
        const int peerCount = 4;
        var clientSides = new List<IRpcChannel>();
        var serverSides = new List<IRpcChannel>();
        for (var i = 0; i < peerCount; i++)
        {
            var (client, server) = InMemoryChannel.CreatePair();
            clientSides.Add(client);
            serverSides.Add(server);
        }

        var hostPeers = new ConcurrentBag<RpcPeer>();
        var connectedCount = 0;
        var allConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = RpcHost
            .Listen(new MultiConnectionServerTransport(serverSides), NewSerializer())
            .ForEachPeer(peer => peer.Provide<IGameService>(new TestGameService()));
        host.PeerConnected += (_, args) =>
        {
            hostPeers.Add(args.Peer);
            if (Interlocked.Increment(ref connectedCount) == peerCount)
            {
                allConnected.TrySetResult(true);
            }
        };
        await host.StartAsync();

        var clients = clientSides
            .Select(c => RpcPeer.Over(c, NewSerializer(), new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) }).Start())
            .ToList();
        try
        {
            // Every accepted peer is independently callable.
            var statuses = await Task.WhenAll(clients.Select(c => c.GetGameService().GetServerStatusAsync()))
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.All(statuses, Assert.NotNull);

            await allConnected.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(peerCount, Volatile.Read(ref connectedCount));

            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));

            // StopAsync must close every accepted peer.
            Assert.Equal(peerCount, hostPeers.Count);
            Assert.All(hostPeers, peer => Assert.False(peer.IsConnected));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task RpcPeer_HandlesManyConcurrentCalls_OverOneConnection()
    {
        var (clientChannel, serverChannel) = InMemoryChannel.CreatePair();

        await using var provider = RpcPeer.Over(serverChannel, NewSerializer())
            .Provide<IGameService>(new TestGameService())
            .Start();
        await using var caller = RpcPeer.Over(clientChannel, NewSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(10) })
            .Start();

        var game = caller.GetGameService();
        const int callCount = 50;

        var players = await Task.WhenAll(
                Enumerable.Range(0, callCount).Select(i => game.RegisterPlayerAsync($"Player-{i}")))
            .WaitAsync(TimeSpan.FromSeconds(10));

        for (var i = 0; i < callCount; i++)
        {
            Assert.Equal($"Player-{i}", players[i].Name);
        }
    }

}
