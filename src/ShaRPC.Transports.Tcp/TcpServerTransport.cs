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
            _listener = listener;
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
        // so the listener stays alive. AcceptAsync is driven by a single accept loop, so there is no
        // concurrent caller racing _pendingAccept.
        var acceptTask = _pendingAccept ?? listener.AcceptTcpClientAsync();
        _pendingAccept = null;

        if (ct.CanBeCanceled && !acceptTask.IsCompleted)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
                cancelled);

            var completed = await Task.WhenAny(acceptTask, cancelled.Task).ConfigureAwait(false);
            if (completed != acceptTask)
            {
                _pendingAccept = acceptTask;
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

        return new TcpConnection(client, FrameReadIdleTimeout);
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
}
