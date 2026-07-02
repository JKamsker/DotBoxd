using System.Net;
using DotBoxD.Kernels.Game.Server.PluginCatalog;
using DotBoxD.Kernels.Game.Server.Simulation;
using DotBoxD.Pushdown.Services;
using DotBoxD.Transports.Tcp;
using PluginServer = DotBoxD.Plugins.PluginServer;

namespace DotBoxD.Kernels.Game.Server.Ipc;

internal sealed class GameClientConnectionHost : IAsyncDisposable
{
    private readonly PluginConnectionHost<GameClientControlService> _host;

    private GameClientConnectionHost(
        PluginConnectionHost<GameClientControlService> host,
        int port)
    {
        _host = host;
        Port = port;
    }

    public int Port { get; }

    public Task<GameClientControlService> Connected => _host.Connected;

    public Task Disconnected => _host.Disconnected;

    public static async Task<GameClientConnectionHost> StartAsync(
        PluginServer server,
        GameCommandSink sink,
        GameWorld world,
        ClientOperationRegistry operations,
        int port = 0)
    {
        var transport = new TcpServerTransport(IPAddress.Loopback, port);
        var host = await PluginConnectionHost<GameClientControlService>.StartAsync(
            server,
            transport,
            (peer, session) =>
            {
                var callback =
                    global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.GetPluginEventCallback(peer);
                var service = new GameClientControlService(
                    server,
                    session,
                    sink,
                    world,
                    operations,
                    "player-1",
                    callback);
                GameClientFeedForwarder.Attach(world, callback);
                global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvideGameClientControlService(
                    peer,
                    service);
                global::DotBoxD.Services.Generated.DotBoxDGeneratedExtensions.ProvideGameWorldView(
                    peer,
                    new GameWorldView(world));
                return service;
            }).ConfigureAwait(false);
        return new GameClientConnectionHost(
            host,
            transport.LocalEndpoint?.Port ?? throw new InvalidOperationException("TCP listener did not bind."));
    }

    public Task StopAsync() => _host.StopAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
