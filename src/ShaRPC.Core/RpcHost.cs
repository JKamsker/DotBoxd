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
        }

        try
        {
            await _listener.StartAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                    _stopTask = null;
                    cts.Dispose();
                }

                _starting = false;
            }

            throw;
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
            else if (_stopTask is not null || cts.IsCancellationRequested)
            {
                stopStartedListener = true;
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
            if (stopStartedListener)
            {
                await _listener.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _starting = false;
            }
        }

        if (startFailure is not null)
        {
            if (disposeCts)
            {
                cts.Dispose();
            }

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

            await _listener.StopAsync(ct).ConfigureAwait(false);
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

    private async Task AddPeerAsync(IConnection connection)
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
        _peers.Add(peer);
        peer.Start();
        RpcEventHandlerInvoker.Raise(PeerConnected, this, new RpcPeerEventArgs(peer));
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
            await _listener.DisposeAsync().ConfigureAwait(false);
        }
    }
}
