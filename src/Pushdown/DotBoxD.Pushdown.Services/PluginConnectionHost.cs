using DotBoxD.Plugins;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Server;
using DotBoxD.Services.Transport;

namespace DotBoxD.Pushdown.Services;

/// <summary>
/// Accepts ONE plugin connection and owns the per-connection trust-boundary lifecycle: it listens for the
/// connecting peer, mints a <see cref="PluginSession"/> for it, runs <c>configure</c> to provision that peer
/// (provide host services over the session and return the per-connection object callers await), and
/// <b>disposes the session</b> — revoking and unregistering the kernels that peer owned — when the peer
/// disconnects OR the host is stopped/disposed.
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
    private PluginSession? _session;
    private int _accepted;
    private int _sessionDisposed;

    private PluginConnectionHost(string pipeName) => PipeName = pipeName;

    /// <summary>The named pipe the plugin peer dials, or empty when started over a non-pipe transport.</summary>
    public string PipeName { get; }

    /// <summary>Completes when a plugin connects, yielding the object <c>configure</c> returned for it; faults if <c>configure</c> throws.</summary>
    public Task<TConnection> Connected => _connected.Task;

    /// <summary>Completes when the connected plugin drops, after its session has been disposed.</summary>
    public Task Disconnected => _disconnected.Task;

    /// <summary>
    /// Starts listening on <paramref name="pipeName"/> (a convenience over the transport overload; keeps the
    /// high-entropy pipe-name validation). For the connecting peer the host mints a session and invokes
    /// <paramref name="configure"/>(peer, session) — where the caller provides its host services over the session
    /// and returns the per-connection object surfaced by <see cref="Connected"/>. The session is disposed
    /// automatically when the peer disconnects or the host is stopped/disposed.
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
            // Single-connection contract: service only the FIRST peer. The transport may accept more (a named
            // pipe allows multiple instances by default), so a later peer is left un-provisioned — no session,
            // no services — and can therefore neither install nor invoke anything.
            if (Interlocked.Exchange(ref self._accepted, 1) != 0)
            {
                return;
            }

            var session = server.CreateSession();
            self._session = session;
            peer.Disconnected += (_, _) =>
            {
                self.DisposeSessionOnce();        // revoke + unregister the kernels this peer owned
                self._disconnected.TrySetResult();
            };

            // The caller provides its services over the session (before the peer starts) and returns whatever it
            // wants to await on Connected. If it throws, dispose the just-minted session and surface the failure
            // on Connected/Disconnected instead of leaking the session and letting callers time out.
            try
            {
                var connection = configure(peer, session);
                self._connected.TrySetResult(connection);
            }
            catch (Exception ex)
            {
                self.DisposeSessionOnce();
                self._connected.TrySetException(ex);
                self._disconnected.TrySetException(ex);
            }
        });
        await self._host.StartAsync().ConfigureAwait(false);
        return self;
    }

    private void DisposeSessionOnce()
    {
        if (Interlocked.Exchange(ref _sessionDisposed, 1) == 0)
        {
            _session?.Dispose();
        }
    }

    /// <summary>
    /// Stops accepting connections and disposes the connected plugin's session. A local stop does not raise the
    /// peer's <c>Disconnected</c> event, so this is the cleanup path when the host shuts the listener down while
    /// a plugin is still connected.
    /// </summary>
    public async Task StopAsync()
    {
        await _host.StopAsync().ConfigureAwait(false);
        DisposeSessionOnce();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync().ConfigureAwait(false);
        DisposeSessionOnce();
    }
}
