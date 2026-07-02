using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Server;

/// <summary>
/// Accepts connections from a listener and turns each one into an <see cref="RpcPeer"/>. The
/// accept loop that used to live inside the server now lives here, and its output is peers:
/// because each connection is a full peer, a host can both provide services to and call back
/// into the peers that connect to it.
/// </summary>
public sealed partial class RpcHost : IAsyncDisposable
{
    private readonly IServerTransport _listener;
    private readonly ISerializer _serializer;
    private readonly RpcPeerOptions _options;
    private readonly RpcHostAcceptLoop _acceptLoop;
    private readonly object _lifecycleLock = new();
    private readonly RpcHostPeerConfiguration _configure = new();
    private readonly RpcHostPeerAdmission _admission;
    private readonly RpcHostPeerCollection _peers = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _stopTask;
    private bool _starting;
    private int _disposed;
    private int _listenerStopped;

    // Test seam: invoked after _listener.StartAsync succeeds but before StartAsync's second
    // lifecycle lock. Null (inert) in production. Lets a test deterministically run StopCoreAsync
    // to completion in the gap so the second lock observes a cleared _cts.
    internal Func<Task>? _onListenerStartedForTest;

    private RpcHost(IServerTransport listener, ISerializer serializer, RpcPeerOptions options)
    {
        _listener = listener;
        _serializer = serializer;
        _options = options;
        _admission = new RpcHostPeerAdmission(options.MaxAcceptedPeers);
        _acceptLoop = new RpcHostAcceptLoop(listener, AddPeerAsync, RaiseAcceptError);
    }

    /// <summary>Creates a host that turns every accepted connection into a peer.</summary>
    public static RpcHost Listen(IServerTransport listener, ISerializer serializer, RpcPeerOptions? options = null)
    {
        if (listener is null)
        {
            throw new ArgumentNullException(nameof(listener));
        }

        if (serializer is null)
        {
            throw new ArgumentNullException(nameof(serializer));
        }

        return new RpcHost(listener, serializer, options ?? new RpcPeerOptions());
    }

    /// <summary>Registers configuration that runs for every accepted peer before its read loop
    /// starts. Use it to <see cref="RpcPeer.Provide{TService}(TService)"/> exports (and optionally
    /// <see cref="RpcPeer.Get{TService}"/> proxies to call the peer back).</summary>
    /// <remarks>
    /// Services provided here are callable by any accepted peer. DotBoxD does not add
    /// authentication or authorization; enforce access control at the transport or application
    /// layer.
    /// </remarks>
    public RpcHost ForEachPeer(Action<RpcPeer> configure)
    {
        _configure.Add(configure ?? throw new ArgumentNullException(nameof(configure)));
        return this;
    }

    /// <summary>Raised after a connection is accepted and configured.</summary>
    public event EventHandler<RpcPeerEventArgs>? PeerConnected;

    /// <summary>Raised when an accepted peer's read loop ends.</summary>
    public event EventHandler<RpcPeerEventArgs>? PeerDisconnected;

    /// <summary>Raised when the host accept loop catches a non-cancellation exception.</summary>
    public event EventHandler<RpcHostErrorEventArgs>? AcceptError;

    private async Task AddPeerAsync(IRpcChannel connection)
    {
        if (!_admission.TryAcquire(out var admission))
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            return;
        }

        RpcPeer peer;
        try
        {
            peer = RpcPeer.Over(connection, _serializer, _options);
        }
        catch
        {
            admission.Dispose();
            throw;
        }

        var configure = _configure.Snapshot();
        try
        {
            foreach (var configurePeer in configure)
            {
                configurePeer(peer);
            }
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Accepted peer configuration failed", ex);
            RpcEventHandlerInvoker.Raise(AcceptError, this, new RpcHostErrorEventArgs(ex));
            admission.Dispose();
            await peer.DisposeAsync().ConfigureAwait(false);
            return;
        }

        peer.Disconnected += OnPeerDisconnected;

        bool registered;
        lock (_lifecycleLock)
        {
            // Only register a peer the host will still manage. StopCoreAsync drains in-flight
            // hand-offs before CloseAllAsync, so a peer registered here is guaranteed to be closed by
            // the host; one rejected here (the host is stopping/stopped/disposed) is disposed below
            // instead of leaking its channel and read loop past shutdown.
            registered = Volatile.Read(ref _disposed) == 0 && _stopTask is null && _cts is not null;
            if (registered)
            {
                registered = _peers.TryAdd(peer, admission);
            }
        }

        if (!registered)
        {
            peer.Disconnected -= OnPeerDisconnected;
            admission.Dispose();
            await peer.DisposeAsync().ConfigureAwait(false);
            return;
        }

        // Raise PeerConnected BEFORE starting the read loop. peer.Start() launches the read loop,
        // which on an already-closed channel immediately fires Disconnected -> PeerDisconnected; doing
        // it before this event could surface PeerDisconnected ahead of PeerConnected for the same peer.
        // StopCoreAsync.DrainInFlightAsync awaits this hand-off, so the peer is still started before
        // the host drains and closes its peers.
        try
        {
            RpcEventHandlerInvoker.Raise(PeerConnected, this, new RpcPeerEventArgs(peer));
            peer.Start();
        }
        catch (ObjectDisposedException)
        {
            // A PeerConnected handler disposed the peer (a documented access-control gesture). peer.Start()
            // then throws on the disposed peer — this is not an accept/transport failure, so do NOT raise
            // AcceptError. Unsubscribe and drop it from the host's collection (the read loop never started,
            // so OnPeerDisconnected will never run to do this); DisposeAsync is idempotent and awaits the
            // handler-started cleanup if it is already in flight.
            peer.Disconnected -= OnPeerDisconnected;
            _peers.Remove(peer);
            await peer.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            peer.Disconnected -= OnPeerDisconnected;
            _peers.Remove(peer);
            await peer.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void RaiseAcceptError(Exception ex) =>
        RpcEventHandlerInvoker.Raise(AcceptError, this, new RpcHostErrorEventArgs(ex));

    private void OnPeerDisconnected(object? sender, RpcDisconnectedEventArgs args)
    {
        if (sender is not RpcPeer peer)
        {
            return;
        }

        peer.Disconnected -= OnPeerDisconnected;
        _peers.Remove(peer);
        RpcEventHandlerInvoker.Raise(PeerDisconnected, this, new RpcPeerEventArgs(peer));

        // Dispose off the read-loop callback so DisposeAsync can await the now-completing loop
        // without deadlocking on itself.
        _peers.DisposeInBackground(peer);
    }

}
