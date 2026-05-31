namespace ShaRPC.Core.Transport;

/// <summary>
/// Client transport over an already-established connection.
/// </summary>
public sealed class SingleConnectionTransport : ITransport
{
    private readonly IConnection _connection;
    private readonly bool _ownsConnection;
    private int _disposed;

    public SingleConnectionTransport(IConnection connection, bool ownsConnection = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _ownsConnection = ownsConnection;
    }

    public IConnection? Connection => Volatile.Read(ref _disposed) == 0 ? _connection : null;

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
    private readonly IConnection _connection;
    private readonly bool _ownsConnection;
    private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _accepted;
    private int _started;
    private int _disposed;

    public SingleConnectionServerTransport(IConnection connection, bool ownsConnection = false)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _ownsConnection = ownsConnection;
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SingleConnectionServerTransport));
        }

        Interlocked.Exchange(ref _started, 1);
        return Task.CompletedTask;
    }

    public async Task<IConnection> AcceptAsync(CancellationToken ct = default)
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

        using (ct.Register(static state =>
            ((TaskCompletionSource<bool>)state!).TrySetResult(true), _stopped))
        {
            await _stopped.Task.ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        throw new OperationCanceledException();
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
