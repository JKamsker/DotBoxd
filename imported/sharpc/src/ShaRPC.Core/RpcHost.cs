using ShaRPC.Core.Serialization;
using ShaRPC.Core.Transport;

namespace ShaRPC.Core;

/// <summary>
/// Accepts connections from a listener and turns each one into an <see cref="RpcPeer"/>. The
/// accept loop that used to live inside the server now lives here, and its output is peers:
/// because each connection is a full peer, a host can both provide services to and call back
/// into the peers that connect to it.
/// </summary>
public sealed class RpcHost : IAsyncDisposable
{
    private readonly IServerTransport _listener;
    private readonly ISerializer _serializer;
    private readonly RpcPeerOptions _options;
    private readonly RpcHostAcceptLoop _acceptLoop;
    private readonly object _lifecycleLock = new();
    private readonly RpcHostPeerConfiguration _configure = new();
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
    /// Services provided here are callable by any accepted peer. ShaRPC does not add
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

    public async Task StartAsync(CancellationToken ct = default)
    {
        CancellationTokenSource cts;
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(RpcHost));
            }

            if (_cts is not null || _starting)
            {
                throw new InvalidOperationException("Host is already running.");
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            _starting = true;

            // Fresh lifecycle: arm the single-shot listener-stop guard so exactly one of StartAsync's
            // recovery path and StopCoreAsync stops the listener this run.
            _listenerStopped = 0;
        }

        try
        {
            // Start under cts.Token (linked to the caller ct), not the bare caller ct, so a concurrent
            // StopAsync/DisposeAsync — which cancels cts — can interrupt a transport whose StartAsync
            // blocks (e.g. waiting on an OS resource) instead of hanging shutdown until it returns.
            await _listener.StartAsync(cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            bool disposed;
            lock (_lifecycleLock)
            {
                disposed = Volatile.Read(ref _disposed) != 0;

                // Only reclaim lifecycle state if no StopCoreAsync is in flight. A concurrent
                // StopAsync may have already installed _stopTask (and cancelling its cts is what
                // interrupted this start); nulling _cts/_stopTask or disposing cts here would orphan
                // that running stop — its finally would then skip cleanup (ReferenceEquals(_cts, cts)
                // is false) and a later DisposeAsync would see _cts == null, return immediately, and
                // dispose the transport while the orphan is still inside _listener.StopAsync. Leaving
                // the state intact lets DisposeAsync's StopAsync observe and await the in-flight stop.
                if (ReferenceEquals(_cts, cts) && _stopTask is null)
                {
                    _cts = null;
                    cts.Dispose();
                }

                _starting = false;
            }

            // If the start was interrupted because the host was disposed, surface the disposed contract
            // rather than the raw cancellation from the now-cancelled cts.Token — keeps StartAsync's
            // behaviour stable for callers while still letting Stop/Dispose cancel a cooperative start.
            if (disposed && ex is OperationCanceledException)
            {
                throw new ObjectDisposedException(nameof(RpcHost));
            }

            throw;
        }

        var onListenerStarted = _onListenerStartedForTest;
        if (onListenerStarted is not null)
        {
            await onListenerStarted().ConfigureAwait(false);
        }

        var stopStartedListener = false;
        Exception? startFailure = null;
        var disposeCts = false;
        lock (_lifecycleLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                var ownsCts = ReferenceEquals(_cts, cts);
                disposeCts = ownsCts && _stopTask is null;
                if (ownsCts)
                {
                    _cts = null;
                    _stopTask = null;
                }

                stopStartedListener = true;
                startFailure = new ObjectDisposedException(nameof(RpcHost));
            }
            else if (!ReferenceEquals(_cts, cts))
            {
                stopStartedListener = true;
            }
            else if (_stopTask is not null)
            {
                // A StopAsync is in flight; it owns _cts and will clean up. We only need to ensure the
                // listener we started is stopped (guarded below so it is not stopped twice).
                stopStartedListener = true;
            }
            else if (cts.IsCancellationRequested)
            {
                // The caller token fired after the transport started but before the accept loop launched,
                // and no StopAsync is in flight. Reclaim our own lifecycle state (mirroring the catch
                // block) so the cancelled, undisposed CTS does not linger and make a later StartAsync
                // wrongly report "Host is already running."
                disposeCts = ReferenceEquals(_cts, cts);
                if (disposeCts)
                {
                    _cts = null;
                }

                stopStartedListener = true;
                startFailure = new InvalidOperationException("Host start was stopped before it completed.");
            }
            else
            {
                _starting = false;
                _acceptTask = _acceptLoop.RunAsync(cts.Token);
                return;
            }
        }

        try
        {
            // Stop the listener at most once across this lifecycle: a concurrent or already-completed
            // StopCoreAsync may have stopped it too, and IServerTransport does not require StopAsync to
            // be safe to call twice. The guard is re-armed per StartAsync.
            if (stopStartedListener && Interlocked.Exchange(ref _listenerStopped, 1) == 0)
            {
                await _listener.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception stopEx)
        {
            // Best-effort cleanup of a listener we started but will not use: do not let its failure mask
            // the real start outcome (startFailure), nor unwind past the disposal below and leak the
            // linked CTS. Surface it to diagnostics instead.
            RpcDiagnostics.Report("Listener stop during start recovery failed", stopEx);
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _starting = false;
            }

            if (disposeCts)
            {
                cts.Dispose();
            }
        }

        if (startFailure is not null)
        {
            throw startFailure;
        }

        throw new InvalidOperationException("Host start was stopped before it completed.");
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_lifecycleLock)
        {
            if (_cts is null)
            {
                return Task.CompletedTask;
            }

            return _stopTask ??= StopCoreAsync(_cts, _acceptTask, ct);
        }
    }

    private async Task StopCoreAsync(CancellationTokenSource cts, Task? acceptTask, CancellationToken ct)
    {
        var completed = false;
        try
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // CTS was disposed by a prior failed stop attempt.
            }

            if (acceptTask is not null)
            {
                try
                {
                    await acceptTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RpcDiagnostics.Report("Accept loop fault during shutdown", ex);
                }
            }

            // Await peer hand-offs the (now-stopped) accept loop started so a connection accepted
            // just before cancellation finishes registering before we drain peers — otherwise it
            // could start a peer after CloseAllAsync, leaking the channel past host shutdown.
            await _acceptLoop.DrainInFlightAsync().ConfigureAwait(false);

            // Stop the listener at most once per lifecycle (see StartAsync's recovery path): the flag is
            // armed by StartAsync, so whichever of the two paths runs first performs the single stop.
            if (Interlocked.Exchange(ref _listenerStopped, 1) == 0)
            {
                try
                {
                    await _listener.StopAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    // The stop did not complete (e.g. the supplied token was already cancelled); re-arm
                    // so a retried StopAsync can stop the listener again.
                    Volatile.Write(ref _listenerStopped, 0);
                    throw;
                }
            }

            await _peers.CloseAllAsync().ConfigureAwait(false);
            await _peers.AwaitCleanupAsync().ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by a prior failed stop attempt.
            }

            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _stopTask = null;

                    if (completed)
                    {
                        _cts = null;
                        _acceptTask = null;
                    }
                }
            }
        }
    }

    private async Task AddPeerAsync(IRpcChannel connection)
    {
        var peer = RpcPeer.Over(connection, _serializer, _options);
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
                _peers.Add(peer);
            }
        }

        if (!registered)
        {
            peer.Disconnected -= OnPeerDisconnected;
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
            // so OnPeerDisconnected will never run to do this); the peer/channel disposal is already in
            // flight from the handler.
            peer.Disconnected -= OnPeerDisconnected;
            _peers.Remove(peer);
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                // If StopAsync threw (e.g. the transport's listener stop faulted), its own peer teardown
                // was skipped and the _disposed guard blocks any retry — so close accepted peers here.
                // Idempotent on the normal path: StopCoreAsync already closed them and CloseAllAsync is a
                // no-op on an empty collection.
                await _peers.CloseAllAsync().ConfigureAwait(false);
                await _peers.AwaitCleanupAsync().ConfigureAwait(false);
            }
            finally
            {
                await _listener.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
