using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Pushdown.Services;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// Builds the game's plugin connection. The per-connection IPC ceremony — listen on a high-entropy pipe, mint a
/// session per peer, dispose it on disconnect, surface connected/disconnected — now lives in the framework's
/// <see cref="PluginConnectionHost{TConnection}"/>. This factory keeps only the genuinely connection-specific
/// work: choosing which services to provide for the peer (the reverse event-callback proxy plus the two
/// generated <c>[RpcService]</c> impls — the control plane and the world surface).
/// </summary>
internal static class GamePluginHost
{
    public static Task<PluginConnectionHost<GamePluginControlService>> StartAsync(
        PluginServer server,
        GameCommandSink sink,
        GameWorld world)
        => PluginConnectionHost<GamePluginControlService>.StartAsync(
            server,
            "dotboxd-game-" + Guid.NewGuid().ToString("N"),
            (peer, session) =>
            {
                // Reverse-direction proxy: the plugin PROVIDES IPluginEventCallback, the server GETS it to push
                // filtered+projected values back for remote RunLocal chains over the same bidirectional pipe.
                var eventCallback =
                    global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.GetPluginEventCallback(peer);
                var service = new GamePluginControlService(server, session, sink, world, eventCallback);

                // Two services per connection: the control-plane (install IR, settings, hold) and the domain
                // world surface. ProvideGameWorldAccess is generated from [RpcService] on the interface.
                global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvideGamePluginControlService(
                    peer,
                    service);
                global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvideGameWorldAccess(
                    peer,
                    new GameWorldAccess(world));
                return service;
            });
}
