using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Buffers;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using DotBoxD.Services.Transport;
using Shared;
using Xunit;

namespace DotBoxD.Services.Tests.Peer;

public sealed class RpcPeerSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ConnectPeerAsync_ConnectsStartsAndDisposesTransport()
    {
        var serializer = new MessagePackRpcSerializer();
        var (clientChannel, serverChannel) = InMemoryPipe.CreateConnectionPair();
        var transport = new TrackingTransport(clientChannel);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverChannel, ownsConnection: true), serializer)
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        await host.StartAsync();

        RpcPeerSession? session;
        await using (session = await transport
            .ConnectPeerAsync(serializer, new RpcPeerOptions { RequestTimeout = Timeout }))
        {
            var status = await session.Get<IGameService>().GetServerStatusAsync();
            Assert.NotNull(status);
            Assert.True(session.IsConnected);
            Assert.True(transport.ConnectCalled);
        }

        Assert.True(transport.Disposed);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task ConnectPeerAsync_ConfiguresPeerBeforeReadLoopStarts()
    {
        var serializer = new MessagePackRpcSerializer();
        var greeted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (clientChannel, serverChannel) = InMemoryPipe.CreateConnectionPair();
        var transport = new TrackingTransport(clientChannel);

        await using var host = RpcHost
            .Listen(new SingleConnectionServerTransport(serverChannel, ownsConnection: true), serializer)
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => _ = GreetAsync(args.Peer, greeted);
        await host.StartAsync();

        await using var session = await transport.ConnectPeerAsync(
            serializer,
            peer => peer.ProvidePlayerNotifications(new RecordingNotifications("dotboxd-plugin")),
            new RpcPeerOptions { RequestTimeout = Timeout });

        var status = await session.Get<IGameService>().GetServerStatusAsync();
        var identity = await greeted.Task.WaitAsync(Timeout);

        Assert.NotNull(status);
        Assert.Equal("dotboxd-plugin", identity);
    }

    [Fact]
    public async Task ConnectPeerAsync_DisposesTransportWhenConfigurationFails()
    {
        var serializer = new MessagePackRpcSerializer();
        var (clientChannel, serverChannel) = InMemoryPipe.CreateConnectionPair();
        var transport = new TrackingTransport(clientChannel);
        var failure = new InvalidOperationException("configuration failed");

        try
        {
            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                transport.ConnectPeerAsync(serializer, _ => throw failure));

            Assert.Same(failure, thrown);
            Assert.True(transport.Disposed);
            Assert.False(clientChannel.IsConnected);
        }
        finally
        {
            await serverChannel.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConnectPeerAsync_WithPreCanceledTokenDoesNotInvokeTransport()
    {
        var serializer = new MessagePackRpcSerializer();
        var channel = new TrackingChannel();
        var transport = new TrackingTransport(channel, ignoreCancellation: true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        RpcPeerSession? session = null;
        var exception = await Record.ExceptionAsync(async () =>
        {
            session = await transport.ConnectPeerAsync(serializer, options: null, ct: cts.Token);
        });

        if (session is not null)
        {
            await session.DisposeAsync();
        }

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.False(transport.ConnectCalled);
        Assert.Null(transport.Connection);
        Assert.False(channel.ReceiveCalled);
        Assert.False(channel.SendCalled);
    }

    private static async Task GreetAsync(RpcPeer peer, TaskCompletionSource<string> done)
    {
        try
        {
            done.TrySetResult(await peer.GetPlayerNotifications().WhoAmIAsync());
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
    }

    private sealed class RecordingNotifications : IPlayerNotifications
    {
        private readonly string _identity;

        public RecordingNotifications(string identity) => _identity = identity;

        public Task NotifyAsync(string message, CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> WhoAmIAsync(CancellationToken ct = default) => Task.FromResult(_identity);
    }

    private sealed class TrackingTransport : ITransport
    {
        private readonly IRpcChannel _connection;
        private readonly bool _ignoreCancellation;

        public TrackingTransport(IRpcChannel connection, bool ignoreCancellation = false)
        {
            _connection = connection;
            _ignoreCancellation = ignoreCancellation;
        }

        public bool ConnectCalled { get; private set; }

        public bool Disposed { get; private set; }

        public IRpcChannel? Connection { get; private set; }

        public bool IsConnected => !Disposed && Connection?.IsConnected == true;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            if (!_ignoreCancellation)
            {
                ct.ThrowIfCancellationRequested();
            }

            ConnectCalled = true;
            Connection = _connection;
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            Disposed = true;
            await _connection.DisposeAsync();
        }
    }

    private sealed class TrackingChannel : IRpcChannel
    {
        public bool ReceiveCalled { get; private set; }

        public bool SendCalled { get; private set; }

        public bool IsConnected { get; private set; } = true;

        public string RemoteEndpoint => "memory://tracking";

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            SendCalled = true;
            return Task.CompletedTask;
        }

        public Task<Payload> ReceiveAsync(CancellationToken ct = default)
        {
            ReceiveCalled = true;
            return Task.FromResult(Payload.Empty);
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
