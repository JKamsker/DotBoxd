using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Pushdown.Services;
using DotBoxD.Services.Server;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

/// <summary>
/// Wraps the per-connection IPC ceremony for accepting ONE plugin: listen on a high-entropy pipe, mint an
/// ownership session per peer, provide BOTH services (the control-plane and the domain
/// <see cref="GameWorldAccess"/>), and unload the peer's kernels on disconnect. Collapses what used to be a
/// pipe-name + two TaskCompletionSources + session + provision + disconnect block in Program.cs, so the
/// sample reads as phases, not plumbing.
/// </summary>
internal sealed class GamePluginHost : IAsyncDisposable
{
    private readonly TaskCompletionSource<GamePluginControlService> _connected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _disconnected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RpcHost _host = null!;

    private GamePluginHost(string pipeName) => PipeName = pipeName;

    /// <summary>The high-entropy pipe name the plugin child process should dial.</summary>
    public string PipeName { get; }

    /// <summary>Completes when a plugin connects, yielding its control-plane service.</summary>
    public Task<GamePluginControlService> Connected => _connected.Task;

    /// <summary>Completes when the connected plugin drops (after its kernels are unloaded).</summary>
    public Task Disconnected => _disconnected.Task;

    public static async Task<GamePluginHost> StartAsync(PluginServer server, GameCommandSink sink, GameWorld world)
    {
        var self = new GamePluginHost("dotboxd-game-" + Guid.NewGuid().ToString("N"));
        self._host = RpcMessagePackIpc.ListenNamedPipe(
            self.PipeName,
            peer =>
            {
                var session = server.CreateSession();
                var service = new GamePluginControlService(server, session, sink, world);
                peer.Disconnected += (_, _) =>
                {
                    session.Dispose();                  // revoke + unregister the kernels this peer owned
                    self._disconnected.TrySetResult();
                };

                // Two services per connection: the control-plane (install IR, settings, hold) and the domain
                // world surface. ProvideGameWorldAccess is generated from [DotBoxDService] on the interface.
                peer.ProvideGamePluginControlService(service);
                peer.ProvideGameWorldAccess(new GameWorldAccess(world));
                self._connected.TrySetResult(service);
            });
        await self._host.StartAsync().ConfigureAwait(false);
        return self;
    }

    public Task StopAsync() => _host.StopAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
