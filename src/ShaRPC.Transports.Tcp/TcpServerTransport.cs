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
    private bool _started;

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

    public Task StartAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }

        if (_started)
        {
            throw new InvalidOperationException("Server already started.");
        }

        _listener = new TcpListener(_address, _port);
        _listener.Start();
        _started = true;

        return Task.CompletedTask;
    }

    public async Task<IConnection> AcceptAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(TcpServerTransport));
        }

        if (_listener == null)
        {
            throw new InvalidOperationException("Server not started.");
        }

        // netstandard2.1 has no CancellationToken overload for AcceptTcpClientAsync, and Stop()-ing
        // the listener to unblock would tear it down for every future accept. Instead race the accept
        // against the token; on cancellation keep the in-flight accept to hand back on the next call
        // so the listener stays alive. AcceptAsync is driven by a single accept loop, so there is no
        // concurrent caller racing _pendingAccept.
        var acceptTask = _pendingAccept ?? _listener.AcceptTcpClientAsync();
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

        return new TcpConnection(client);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _listener?.Stop();
        ObservePendingAccept();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        _listener?.Stop();
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
