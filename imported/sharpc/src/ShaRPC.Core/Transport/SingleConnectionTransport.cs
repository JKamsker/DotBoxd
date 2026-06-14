namespace ShaRPC.Core.Transport;

/// <summary>
/// Client transport over an already-established connection.
/// </summary>
public sealed class SingleConnectionTransport : ITransport
{
    private readonly IRpcChannel _connection;
    private readonly bool _ownsConnection;
    private int _disposed;

    public SingleConnectionTransport(IRpcChannel connection, bool ownsConnection = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _ownsConnection = ownsConnection;
    }

    public IRpcChannel? Connection => Volatile.Read(ref _disposed) == 0 ? _connection : null;

    public bool IsConnected => Volatile.Read(ref _disposed) == 0 && _connection.IsConnected;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SingleConnectionTransport));
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsConnection)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Server transport that accepts one already-established connection.
/// </summary>
public sealed class SingleConnectionServerTransport : IServerTransport
{
    private readonly IRpcChannel _connection;
    private readonly bool _ownsConnection;
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _accepted;
    private int _started;
    private int _disposed;

    public SingleConnectionServerTransport(IRpcChannel connection, bool ownsConnection = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _ownsConnection = ownsConnection;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SingleConnectionServerTransport));
        }

        Interlocked.Exchange(ref _started, 1);
        return Task.CompletedTask;
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SingleConnectionServerTransport));
        }

        if (Volatile.Read(ref _started) == 0)
        {
            throw new InvalidOperationException("Transport has not been started.");
        }

        if (Interlocked.Exchange(ref _accepted, 1) == 0)
        {
            return _connection;
        }

        // Honour an already-cancelled token before registering on the shared one-shot _stopped TCS: a
        // pre-cancelled token would otherwise fire ct.Register synchronously, completing _stopped and
        // spuriously unblocking every other parked accept (bricking the transport). Mirrors TcpServerTransport.
        ct.ThrowIfCancellationRequested();

        using (ct.Register(static state =>
            ((TaskCompletionSource<bool>)state!).TrySetResult(true), _stopped))
        {
            await _stopped.Task.ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        // Associate the cancellation with the caller's token even when StopAsync (not the token) released
        // the parked accept, so token-scoped catch filters observe it — matching Tcp/NamedPipe transports.
        throw new OperationCanceledException(ct);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _stopped.TrySetResult(true);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stopped.TrySetResult(true);

        if (_ownsConnection)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
