using Shared;
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

const string Host = "localhost";
const int Port = 5050;

Console.WriteLine("ShaRPC Peer (caller side) Example");
Console.WriteLine("=================================");
Console.WriteLine();

var serializer = new MessagePackRpcSerializer();
var transport = new TcpTransport(Host, Port);

try
{
    Console.WriteLine($"Connecting to {Host}:{Port}...");
    await transport.ConnectAsync();
    Console.WriteLine("Connected!");
    Console.WriteLine();

    // The caller provides a callback the other peer can push notifications to, and gets a
    // proxy to call the game service — both over the one connection.
    await using var peer = RpcPeer
        .Over(transport.Connection!, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(10) })
        .Provide<IPlayerNotifications>(new ConsoleNotifications())
        .Start();

    var gameService = peer.GetGameService();

    Console.WriteLine("Getting server status...");
    var status = await gameService.GetServerStatusAsync();
    Console.WriteLine($"  Players online: {status.PlayerCount}");
    Console.WriteLine($"  Server time: {status.ServerTime}");
    Console.WriteLine($"  Version: {status.Version}");
    Console.WriteLine();

    Console.WriteLine("Registering new player...");
    var playerState = await gameService.RegisterPlayerAsync("TestPlayer");
    Console.WriteLine($"  Player ID: {playerState.PlayerId}");
    Console.WriteLine($"  Name: {playerState.Name}");
    Console.WriteLine($"  Level: {playerState.Level}");
    Console.WriteLine($"  Health: {playerState.Health}/{playerState.MaxHealth}");
    Console.WriteLine($"  Position: ({playerState.PositionX}, {playerState.PositionY}, {playerState.PositionZ})");
    Console.WriteLine();

    Console.WriteLine("Moving player...");
    var moveResult = await gameService.MovePlayerAsync(new MoveRequest
    {
        PlayerId = playerState.PlayerId,
        X = 10.5f,
        Y = 0,
        Z = 20.3f
    });
    Console.WriteLine($"  Success: {moveResult.Success}");
    Console.WriteLine($"  Message: {moveResult.Message}");
    Console.WriteLine();

    Console.WriteLine("Getting updated player state...");
    var updatedState = await gameService.GetPlayerStateAsync(new PlayerId { Id = playerState.PlayerId });
    Console.WriteLine($"  Position: ({updatedState.PositionX}, {updatedState.PositionY}, {updatedState.PositionZ})");
    Console.WriteLine();

    Console.WriteLine("Performing action...");
    var actionResult = await gameService.PerformActionAsync(new ActionRequest
    {
        PlayerId = playerState.PlayerId,
        ActionType = "Attack",
        TargetId = "enemy_1"
    });
    Console.WriteLine($"  Success: {actionResult.Success}");
    Console.WriteLine($"  Message: {actionResult.Message}");
    Console.WriteLine();

    // Give the server a moment to push its welcome callback.
    await Task.Delay(200);

    Console.WriteLine("All RPC calls completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.ToString());
}
finally
{
    await transport.DisposeAsync();
    Console.WriteLine("\nClient disconnected.");
}

/// <summary>Caller-side callback the server peer can push notifications to.</summary>
internal sealed class ConsoleNotifications : IPlayerNotifications
{
    public Task NotifyAsync(string message, CancellationToken ct = default)
    {
        Console.WriteLine($"  [server push] {message}");
        return Task.CompletedTask;
    }

    public Task<string> WhoAmIAsync(CancellationToken ct = default) => Task.FromResult("TestPlayer");
}
