using Server;
using Shared;
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

const int Port = 5050;

Console.WriteLine("ShaRPC Peer (provider side) Example");
Console.WriteLine("===================================");
Console.WriteLine();

var serializer = new MessagePackRpcSerializer();
var gameService = new GameService();

// A host turns every accepted connection into a peer. Each peer provides the game service;
// because the connection is a full peer, the host can also call back into it.
await using var host = RpcHost
    .Listen(new TcpServerTransport(Port), serializer)
    .ForEachPeer(peer => peer.ProvideGameService(gameService));

host.PeerConnected += (_, args) =>
{
    var peer = args.Peer;
    Console.WriteLine($"  [peer connected] {peer.RemoteEndpoint}");
    peer.ReadError += (_, readArgs) => Console.WriteLine($"  [peer read error] {readArgs.Error.Message}");
    _ = GreetAsync(peer);
};

Console.WriteLine($"Starting host on port {Port}...");
await host.StartAsync();
Console.WriteLine("Host started. Press Ctrl+C to stop.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nShutting down...");
}

await host.StopAsync();
Console.WriteLine("Host stopped.");

// Bidirectional: the provider side calls back into the connecting peer over the same connection.
static async Task GreetAsync(RpcPeer peer)
{
    try
    {
        var notifications = peer.GetPlayerNotifications();
        var who = await notifications.WhoAmIAsync();
        await notifications.NotifyAsync($"Welcome, {who}! The server sees you.");
    }
    catch (Exception ex)
    {
        // The connecting peer may not provide IPlayerNotifications.
        Console.WriteLine($"  (no callback channel: {ex.Message})");
    }
}
