using System.Net;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Tests.Support;
using Shared;
using Xunit;
using static DotBoxD.Services.Tests.Peer.PeerTestSupport;

namespace DotBoxD.Services.Tests.Peer;

public sealed class PeerTcpTests
{
    [Fact]
    public async Task RpcPeer_OverTcp_RoundTrips()
    {
        Exception? clientReadError = null;
        Exception? serverReadError = null;
        var serverTransport = new DotBoxD.Transports.Tcp.TcpServerTransport(IPAddress.Loopback, 0);

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

        var transport = new DotBoxD.Transports.Tcp.TcpTransport("127.0.0.1", port);
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
        var serverTransport = new DotBoxD.Transports.Tcp.TcpServerTransport(IPAddress.Loopback, 0);

        // Exercise the generated Provide/Get extension methods (the shape the sample uses).
        await using var host = RpcHost
            .Listen(serverTransport, NewSerializer())
            .ForEachPeer(peer => peer.ProvideGameService(new TestGameService()));
        host.PeerConnected += (_, args) => _ = GreetAsync(args.Peer, greeted);
        await host.StartAsync();
        var port = serverTransport.LocalEndpoint?.Port ??
            throw new InvalidOperationException("TCP test server did not expose a bound port.");

        var transport = new DotBoxD.Transports.Tcp.TcpTransport("127.0.0.1", port);
        await transport.ConnectAsync();
        await using var client = RpcPeer.Over(transport.Connection!, NewSerializer(),
                new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(5) })
            .ProvidePlayerNotifications(new RecordingNotifications("sample-client"))
            .Start();

        var game = client.GetGameService();
        var status = await game.GetServerStatusAsync();
        Assert.NotNull(status);

        var who = await greeted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal("sample-client", who);
    }

    private static async Task GreetAsync(RpcPeer peer, TaskCompletionSource<string> done)
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
