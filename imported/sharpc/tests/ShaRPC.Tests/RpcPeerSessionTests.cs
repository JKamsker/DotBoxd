using ShaRPC.Core;
using ShaRPC.Core.Transport;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using Shared;
using Xunit;

namespace ShaRPC.Tests;

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
            peer => peer.ProvidePlayerNotifications(new RecordingNotifications("safe-ir-plugin")),
            new RpcPeerOptions { RequestTimeout = Timeout });

        var status = await session.Get<IGameService>().GetServerStatusAsync();
        var identity = await greeted.Task.WaitAsync(Timeout);

        Assert.NotNull(status);
        Assert.Equal("safe-ir-plugin", identity);
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

        public TrackingTransport(IRpcChannel connection) => _connection = connection;

        public bool ConnectCalled { get; private set; }

        public bool Disposed { get; private set; }

        public IRpcChannel? Connection { get; private set; }

        public bool IsConnected => !Disposed && Connection?.IsConnected == true;

        public Task ConnectAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
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
}
