using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Net;
using Shared;
using DotBoxd.Services;
using DotBoxd.Services.Buffers;
using DotBoxd.Services.Exceptions;
using DotBoxd.Services.Transport;
using DotBoxd.Services.Generated;
using DotBoxd.Codecs.MessagePack;
using Xunit;

namespace DotBoxd.Services.Tests;

/// <summary>
/// End-to-end coverage for the symmetric <see cref="RpcPeer"/> model over an in-memory
/// duplex channel: one-directional calls, bidirectional calls on a single connection, and
/// the explicit reject-inbound behaviour.
/// </summary>
public sealed class PeerTests
{
    private static MessagePackRpcSerializer NewSerializer() => new();

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
        var ex = await Assert.ThrowsAsync<DotBoxdRpcRemoteException>(() => callback.WhoAmIAsync());
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

        var accepted = await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.All(statuses, Assert.NotNull);

            await allConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(peerCount, Volatile.Read(ref connectedCount));

            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

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

    [Fact]
    public async Task RpcPeer_OverTcp_RoundTrips()
    {
        Exception? clientReadError = null;
        Exception? serverReadError = null;
        var serverTransport = new DotBoxd.Transports.Tcp.TcpServerTransport(IPAddress.Loopback, 0);

        await using var host = RpcHost
            .Listen(serverTransport, NewSerializer())
            .ForEachPeer(peer =>
            {
                peer.Provide<IGameService>(new TestGameService());
                peer.ReadError += (_, args) => serverReadError = args.Error;
            });
        await host.StartAsync();
        var port = serverTransport.LocalEndpoint?.Port ??
            throw new InvalidOperationException("TCP test server did not expose a bound port.");

        var transport = new DotBoxd.Transports.Tcp.TcpTransport("127.0.0.1", port);
        await transport.ConnectAsync();
        await using var client = RpcPeer.Over(transport.Connection!, NewSerializer(),
            new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) });
        client.ReadError += (_, args) => clientReadError = args.Error;
        client.Start();

        var game = client.GetGameService();
        try
        {
            var status = await game.GetServerStatusAsync();
            Assert.NotNull(status);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"call failed: {ex.Message}; clientReadError={clientReadError}; serverReadError={serverReadError}");
        }
    }

    [Fact]
    public async Task RpcPeer_OverTcp_Bidirectional_LikeSample()
    {
        var greeted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTransport = new DotBoxd.Transports.Tcp.TcpServerTransport(IPAddress.Loopback, 0);

        // Exercise the generated Provide/Get extension methods (the shape the sample uses).
        await using var host = RpcHost
            .Listen(serverTransport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => _ = GreetAsync(args.Peer, greeted);
        await host.StartAsync();
        var port = serverTransport.LocalEndpoint?.Port ??
            throw new InvalidOperationException("TCP test server did not expose a bound port.");

        var transport = new DotBoxd.Transports.Tcp.TcpTransport("127.0.0.1", port);
        await transport.ConnectAsync();
        await using var client = RpcPeer.Over(transport.Connection!, NewSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .ProvidePlayerNotifications(new RecordingNotifications("sample-client"))
            .Start();

        var game = client.GetGameService();
        var status = await game.GetServerStatusAsync();
        Assert.NotNull(status);

        var who = await greeted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("sample-client", who);

        static async Task GreetAsync(RpcPeer peer, TaskCompletionSource<string> done)
        {
            try
            {
                var notifications = peer.GetPlayerNotifications();
                var who = await notifications.WhoAmIAsync();
                await notifications.NotifyAsync($"Welcome, {who}!");
                done.TrySetResult(who);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }
    }

    private sealed class RecordingNotifications : IPlayerNotifications
    {
        private readonly string _identity;
        public ConcurrentQueue<string> Messages { get; } = new();

        public RecordingNotifications(string identity) => _identity = identity;

        public Task NotifyAsync(string message, CancellationToken ct = default)
        {
            Messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task<string> WhoAmIAsync(CancellationToken ct = default) => Task.FromResult(_identity);
    }

    /// <summary>An in-process, full-duplex <see cref="IRpcChannel"/> pair backed by two channels.</summary>
    private sealed class InMemoryChannel : IRpcChannel
    {
        private readonly ChannelReader<byte[]> _inbound;
        private readonly ChannelWriter<byte[]> _outbound;
        private readonly string _name;
        private int _disposed;

        private InMemoryChannel(ChannelReader<byte[]> inbound, ChannelWriter<byte[]> outbound, string name)
        {
            _inbound = inbound;
            _outbound = outbound;
            _name = name;
        }

        public static (IRpcChannel A, IRpcChannel B) CreatePair()
        {
            var ab = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
            var ba = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
            var a = new InMemoryChannel(ba.Reader, ab.Writer, "peer-a");
            var b = new InMemoryChannel(ab.Reader, ba.Writer, "peer-b");
            return (a, b);
        }

        public bool IsConnected => Volatile.Read(ref _disposed) == 0;

        public string RemoteEndpoint => _name;

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            _outbound.TryWrite(data.ToArray());
            return Task.CompletedTask;
        }

        public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            try
            {
                var bytes = await _inbound.ReadAsync(ct).ConfigureAwait(false);
                var payload = Payload.Rent(bytes.Length);
                bytes.CopyTo(payload.Memory);
                return payload;
            }
            catch (ChannelClosedException)
            {
                return Payload.Empty;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _outbound.TryComplete();
            }

            return default;
        }
    }

    /// <summary>
    /// Server transport that yields a fixed set of pre-established connections one per accept, then
    /// blocks like a listener with no further clients until stopped or cancelled.
    /// </summary>
    private sealed class MultiConnectionServerTransport : IServerTransport
    {
        private readonly Queue<IRpcChannel> _connections;
        private readonly TaskCompletionSource<bool> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private int _disposed;

        public MultiConnectionServerTransport(IEnumerable<IRpcChannel> connections) =>
            _connections = new Queue<IRpcChannel>(connections);

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(MultiConnectionServerTransport));
            }

            lock (_gate)
            {
                if (_connections.Count > 0)
                {
                    return _connections.Dequeue();
                }
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
