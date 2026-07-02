using DotBoxD.Kernels.Game.Client.Rendering;
using DotBoxD.Kernels.Game.Client.Sandbox;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Peer;
using DotBoxD.Transports.Tcp;

namespace DotBoxD.Kernels.Game.Client;

internal sealed class GameClientRuntime : IAsyncDisposable
{
    private readonly RpcPeerSession _session;
    private readonly IGameWorldView _world;
    private readonly ClientPluginHost _plugins;
    private readonly IGameClientControlService _control;

    private GameClientRuntime(
        RpcPeerSession session,
        IGameWorldView world,
        ClientPluginHost plugins,
        IGameClientControlService control)
    {
        _session = session;
        _world = world;
        _plugins = plugins;
        _control = control;
    }

    public static async ValueTask<GameClientRuntime> ConnectAsync(
        string host,
        int port,
        string pluginsRoot,
        CancellationToken ct = default)
    {
        var pump = new ClientEventPump();
        var session = await RpcMessagePackIpc.ConnectAsync(
            new TcpTransport(host, port),
            peer => global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvidePluginEventCallback(
                peer,
                new ClientFeedCallback(pump)),
            cancellationToken: ct).ConfigureAwait(false);
        var control = session.Get<IGameClientControlService>();
        var world = global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.GetGameWorldView(session.Peer);
        var renderer = new ConsoleHudRenderer();
        var plugins = new ClientPluginHost(renderer, pluginsRoot, control);
        await pump.BindAsync(plugins).ConfigureAwait(false);
        return new GameClientRuntime(session, world, plugins, control);
    }

    public async ValueTask InstallClientBundlesAsync(CancellationToken ct = default)
        => await _plugins.InstallBundlesAsync(ct).ConfigureAwait(false);

    public async ValueTask PrintSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = await _world.GetWorldAsync(ct).ConfigureAwait(false);
        var balance = await _world.GetBalanceAsync("player-1", ct).ConfigureAwait(false);
        Console.WriteLine($"[client] snapshot tick={snapshot.Tick} player-1 gold={balance}");
    }

    public async ValueTask ClaimAsync(string monsterId, CancellationToken ct = default)
    {
        var receipt = await _plugins.ClaimBountyAsync(monsterId, ct).ConfigureAwait(false);
        Console.WriteLine($"[client] claim {monsterId} => {receipt}");
    }

    public async ValueTask CallUnknownOperationAsync(CancellationToken ct = default)
    {
        var receipt = await _control.CallPluginOperationAsync("unknown.operation", "monster-1", ct)
            .ConfigureAwait(false);
        Console.WriteLine($"[client] unknown operation => {receipt}");
    }

    public async ValueTask HoldUntilShutdownAsync(CancellationToken ct = default)
        => await _control.HoldUntilShutdownAsync(ct).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        _plugins.Dispose();
        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
