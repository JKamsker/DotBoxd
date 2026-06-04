using System.Net;
using System.Net.Sockets;
using ShaRPC.Core.Transport;

namespace ShaRPC.Transports.Tcp;

/// <summary>
/// TCP server transport implementation.
/// </summary>
public sealed class TcpServerTransport : IServerTransport
{
    private readonly IPAddress _address;
    private readonly int _port;
    private TcpListener? _listener;
    private Task<TcpClient>? _pendingAccept;
    private int _disposed;
    private int _started;
    private int _freshAcceptStartsForTest;

    public TcpServerTransport(int port) : this(IPAddress.Any, port)
    {
    }

    public TcpServerTransport(IPAddress address, int port)
    {
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _port = port;
    }

    public TcpServerTransport(string address, int port)
    {
        _address = IPAddress.Parse(address);
        _port = port;
    }

    /// <summary>
    /// Gets the bound endpoint after <see cref="StartAsync"/> succeeds.
    /// </summary>
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    /// <summary>
    /// Inter-read idle timeout applied to accepted connections' in-progress frame reads (slow-loris
    /// defense). <see langword="null"/> uses <see cref="TcpConnection.DefaultFrameReadIdleTimeout"/>;
    /// <see cref="Timeout.InfiniteTimeSpan"/> disables it. See <see cref="TcpConnection"/>.
    /// </summary>
    public TimeSpan? FrameReadIdleTimeout { get; init; }

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("Server already started.");
        }

        try
        {
            var listener = new TcpListener(_address, _port);
            listener.Start();

            // Fire the pre-publish seam (null/no-op in production) so a deterministic test can race a
            // concurrent DisposeAsync into the window between starting the listener and publishing it.
            _onListenerStartedBeforePublishForTest?.Invoke();

            _listener = listener;

            // Dekker-style fence + re-check: a DisposeAsync that raced in after the _disposed guard above
            // but before this publish saw a still-null _listener (its Interlocked.Exchange was a no-op), so
            // it never stopped the listener we just published. Detect that and stop it here instead of
            // leaking the bound port. Mirrors the client-side TcpTransport.ConnectAsync fix.
            Interlocked.MemoryBarrier();
            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Exchange(ref _listener, null);
                listener.Stop();
                Volatile.Write(ref _started, 0);
                throw new ObjectDisposedException(nameof(TcpServerTransport));
            }
        }
        catch
        {
            // Bind/listen failed (e.g. port in use). Reset so the transport can be started again
            // and a half-constructed listener is not left in the field.
            Volatile.Write(ref _started, 0);
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }

        // Honour an already-cancelled token before claiming or starting any accept, so a pre-cancelled
        // call neither consumes a stashed accept nor starts (and then orphans) a fresh
        // listener.AcceptTcpClientAsync() that the shutdown observation path could never reclaim.
        ct.ThrowIfCancellationRequested();

        // Capture the listener once: a concurrent StopAsync/DisposeAsync nulls the field, and reading
        // it twice could NRE between the guard and the accept call. If Stop races in after this read,
        // AcceptTcpClientAsync simply faults on the stopped listener and the catch below maps it.
        var listener = _listener;
        if (listener == null)
        {
            throw new InvalidOperationException("Server not started.");
        }

        // netstandard2.1 has no CancellationToken overload for AcceptTcpClientAsync, and Stop()-ing
        // the listener to unblock would tear it down for every future accept. Instead race the accept
        // against the token; on cancellation keep the in-flight accept to hand back on the next call
        // so the listener stays alive. AcceptAsync is driven by a single accept loop, but StopAsync/
        // DisposeAsync (any thread) reclaim _pendingAccept via Interlocked, so consume it atomically
        // here too — a plain read+null could let both this call and ObservePendingAccept take the same
        // stashed accept, returning a TcpClient that is also disposed at shutdown.
        var claimed = ClaimPendingAccept();
        Task<TcpClient> acceptTask;
        if (claimed is not null)
        {
            acceptTask = claimed;
        }
        else
        {
            // Count fresh OS-level accepts so a deterministic test can prove that a pre-cancelled token
            // does not start (and orphan) one. Inert in production beyond a single Interlocked increment.
            Interlocked.Increment(ref _freshAcceptStartsForTest);
            acceptTask = listener.AcceptTcpClientAsync();

            // Fire the fresh-accept seam (null/no-op in production) so a deterministic test can race a
            // concurrent cancellation into the window between starting this fresh accept and the in-body
            // IsCancellationRequested check below.
            _onFreshAcceptStartedForTest?.Invoke();
        }

        // Honour an already-cancelled token before the IsCompleted short-circuit below can return a
        // completed (e.g. stashed) accept. If the accept came from the stash, re-stash it first so the
        // in-flight accept (and any socket it completes with) is reclaimed by the shutdown observation
        // path instead of being leaked, mirroring the cancellation re-stash logic below.
        if (ct.IsCancellationRequested)
        {
            // Re-stash whatever accept we hold — a claimed one OR a freshly-started one — so the
            // in-flight accept (and any socket it completes with) is reclaimed by the shutdown
            // observation path instead of being orphaned. A fresh accept (claimed == null) was
            // previously left untracked here, leaking its TcpClient if the OS delivered one.
            _ = Interlocked.Exchange(ref _pendingAccept, acceptTask);
            if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0)
            {
                ObservePendingAccept();
            }

            throw new OperationCanceledException(ct);
        }

        if (ct.CanBeCanceled && !acceptTask.IsCompleted)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancelled);

            var completed = await Task.WhenAny(acceptTask, cancelled.Task).ConfigureAwait(false);
            if (completed != acceptTask)
            {
                // Discard the previous value (always null on this path) — the explicit _ also avoids the
                // CS4014 "un-awaited task" warning that a bare Interlocked.Exchange of a Task would raise.
                _ = Interlocked.Exchange(ref _pendingAccept, acceptTask);

                // If Stop/Dispose already ran ObservePendingAccept before we re-stashed, reclaim now so
                // the in-flight accept (and any socket it completes with) is not leaked past shutdown.
                if (Volatile.Read(ref _started) == 0 || Volatile.Read(ref _disposed) != 0)
                {
                    ObservePendingAccept();
                }

                throw new OperationCanceledException(ct);
            }
        }

        TcpClient client;
        try
        {
            client = await acceptTask.ConfigureAwait(false);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        try
        {
            return new TcpConnection(client, FrameReadIdleTimeout);
        }
        catch
        {
            // The OS socket was already accepted; if TcpConnection construction fails (e.g. an invalid
            // FrameReadIdleTimeout), dispose the client so its socket is not leaked — otherwise the host
            // accept loop's error-retry cycle would leak one socket per iteration. Mirrors the equivalent
            // catch in NamedPipeServerTransport.AcceptAsync.
            client.Dispose();
            throw;
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        // Reset state so the transport can be restarted with StartAsync, and so a subsequent
        // AcceptAsync surfaces "not started" instead of accepting on a stopped listener.
        Volatile.Write(ref _started, 0);
        var listener = Interlocked.Exchange(ref _listener, null);
        listener?.Stop();
        ObservePendingAccept();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        Volatile.Write(ref _started, 0);
        var listener = Interlocked.Exchange(ref _listener, null);
        listener?.Stop();
        ObservePendingAccept();

        return default;
    }

    private void ObservePendingAccept()
    {
        // Reclaim an in-flight accept we stashed on cancellation. Stopping the listener usually
        // faults it (observe the exception), but a client can connect in the window between the
        // cancellation and Stop(), completing the accept with a live TcpClient — close that socket
        // so it is not leaked at shutdown.
        var pending = Interlocked.Exchange(ref _pendingAccept, null);
        _ = pending?.ContinueWith(
            static t =>
            {
                if (t.IsFaulted)
                {
                    _ = t.Exception;
                }
                else if (t.Status == TaskStatus.RanToCompletion)
                {
                    try
                    {
                        t.Result?.Dispose();
                    }
                    catch
                    {
                        // Best-effort close of a socket accepted during shutdown.
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Atomically claims any stashed in-flight accept. Reads the field, fires the test seam (a no-op in
    /// production), then claims the stashed task with a <see cref="Interlocked.CompareExchange{T}"/> —
    /// if a concurrent <see cref="ObservePendingAccept"/> (Stop/Dispose) reclaimed it in between, the CAS
    /// fails and this returns <see langword="null"/> so the caller starts a fresh accept instead of
    /// double-taking the same <see cref="TcpClient"/> that shutdown is disposing.
    /// </summary>
    private Task<TcpClient>? ClaimPendingAccept()
    {
        var stashed = Volatile.Read(ref _pendingAccept);
        _onPendingAcceptConsumeForTest?.Invoke();
        if (stashed is not null && Interlocked.CompareExchange(ref _pendingAccept, null, stashed) == stashed)
        {
            return stashed;
        }

        return null;
    }

    // --- Test seams (null/no-op in production) for the deterministic _pendingAccept double-consume test ---

    /// <summary>Invoked inside <see cref="ClaimPendingAccept"/> between reading and claiming the stash,
    /// so a test can deterministically race a concurrent reclaim into that window.</summary>
    internal Action? _onPendingAcceptConsumeForTest;

    /// <summary>Invoked inside <see cref="AcceptAsync"/> right after a fresh
    /// <c>listener.AcceptTcpClientAsync()</c> is started (and before the in-body cancellation check),
    /// so a test can deterministically cancel the token in that exact window. No-op in production.</summary>
    internal Action? _onFreshAcceptStartedForTest;

    /// <summary>Invoked inside <see cref="StartAsync"/> after <c>listener.Start()</c> but before the
    /// listener is published to <c>_listener</c>, so a test can deterministically race a concurrent
    /// <see cref="DisposeAsync"/> into the publish window. No-op in production.</summary>
    internal Action? _onListenerStartedBeforePublishForTest;

    internal Task<TcpClient>? ClaimPendingAcceptForTest() => ClaimPendingAccept();

    internal void StashPendingAcceptForTest(Task<TcpClient> accept) => Volatile.Write(ref _pendingAccept, accept);

    internal Task<TcpClient>? ReclaimPendingAcceptForTest() => Interlocked.Exchange(ref _pendingAccept, null);

    /// <summary>Number of fresh OS-level accepts started (i.e. not served from the stash). A
    /// pre-cancelled <see cref="AcceptAsync"/> must not start one.</summary>
    internal int FreshAcceptStartsForTest => Volatile.Read(ref _freshAcceptStartsForTest);
}
