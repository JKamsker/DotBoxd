using DotBoxD.Plugins;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;

namespace DotBoxD.Pushdown.Services;

/// <summary>
/// Accepts ONE plugin connection and owns the per-connection trust-boundary lifecycle: it listens for the
/// connecting peer, mints a <see cref="PluginSession"/> for it, runs <c>configure</c> to provision that peer
/// (provide host services over the session and return the per-connection object callers await), and
/// <b>disposes the session when the peer disconnects</b> — revoking and unregistering the kernels that peer
/// owned.
/// <para>
/// This is the per-connection IPC ceremony every plugin host used to hand-write. Forgetting the
/// dispose-on-disconnect step silently leaks a peer's kernels, so the framework owns it; the host is left with
/// only the genuinely connection-specific work of choosing which services to provide. The helper is opt-in:
/// everything it does is reproducible with public API (<see cref="RpcMessagePackIpc.Listen"/> /
/// <see cref="PluginServer.CreateSession"/> / <c>peer.Provide</c> / <c>peer.Disconnected</c>).
/// </para>
/// </summary>
/// <typeparam name="TConnection">The per-connection object <c>configure</c> returns (e.g. the control service).</typeparam>
public sealed class PluginConnectionHost<TConnection> : IAsyncDisposable
{
    private readonly TaskCompletionSource<TConnection> _connected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _disconnected =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RpcHost _host = null!;

    private PluginConnectionHost(string pipeName) => PipeName = pipeName;

    /// <summary>The named pipe the plugin peer dials, or empty when started over a non-pipe transport.</summary>
    public string PipeName { get; }

    /// <summary>Completes when a plugin connects, yielding the object <c>configure</c> returned for it.</summary>
    public Task<TConnection> Connected => _connected.Task;

    /// <summary>Completes when the connected plugin drops, after its session has been disposed.</summary>
    public Task Disconnected => _disconnected.Task;

    /// <summary>
    /// Starts listening on <paramref name="pipeName"/> (a convenience over the transport overload). For the
    /// connecting peer the host mints a session and invokes <paramref name="configure"/>(peer, session) — where
    /// the caller provides its host services over the session and returns the per-connection object surfaced by
    /// <see cref="Connected"/>. The session is disposed automatically when the peer disconnects.
    /// </summary>
    public static Task<PluginConnectionHost<TConnection>> StartAsync(
        PluginServer server,
        string pipeName,
        Func<RpcPeer, PluginSession, TConnection> configure,
        RpcPeerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);
        return StartCoreAsync(
            server,
            pipeName,
            configure,
            configurePeer => RpcMessagePackIpc.ListenNamedPipe(pipeName, configurePeer, options));
    }

    /// <summary>
    /// Starts listening over any <paramref name="transport"/> (TCP, in-memory, …) — the transport-agnostic
    /// form, so a host is not locked to named pipes. Same per-connection lifecycle as the pipe overload;
    /// <see cref="PipeName"/> is empty.
    /// </summary>
    public static Task<PluginConnectionHost<TConnection>> StartAsync(
        PluginServer server,
        IServerTransport transport,
        Func<RpcPeer, PluginSession, TConnection> configure,
        RpcPeerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return StartCoreAsync(
            server,
            pipeName: string.Empty,
            configure,
            configurePeer => RpcMessagePackIpc.Listen(transport, configurePeer, options));
    }

    private static async Task<PluginConnectionHost<TConnection>> StartCoreAsync(
        PluginServer server,
        string pipeName,
        Func<RpcPeer, PluginSession, TConnection> configure,
        Func<Action<RpcPeer>, RpcHost> listen)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(configure);

        var self = new PluginConnectionHost<TConnection>(pipeName);
        self._host = listen(peer =>
        {
            var session = server.CreateSession();
            peer.Disconnected += (_, _) =>
            {
                session.Dispose();               // revoke + unregister the kernels this peer owned
                self._disconnected.TrySetResult();
            };

            // The caller provides its services over the session (in the configurePeer callback, before the
            // peer starts) and returns whatever it wants to await on Connected.
            var connection = configure(peer, session);
            self._connected.TrySetResult(connection);
        });
        await self._host.StartAsync().ConfigureAwait(false);
        return self;
    }

    /// <summary>Stops accepting connections.</summary>
    public Task StopAsync() => _host.StopAsync();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
