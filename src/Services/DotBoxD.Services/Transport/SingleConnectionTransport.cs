namespace DotBoxD.Services.Transport;

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
    private readonly object _sync = new();
    private readonly IRpcChannel _connection;
    private readonly bool _ownsConnection;
    private TaskCompletionSource<bool> _stopped = CreateStoppedSignal();
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

        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SingleConnectionServerTransport));
            }

            if (_started != 0)
            {
                throw new InvalidOperationException("Transport already started.");
            }

            _stopped = CreateStoppedSignal();
            _started = 1;
        }

        return Task.CompletedTask;
    }

    public async Task<IRpcChannel> AcceptAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(SingleConnectionServerTransport));
        }

        Task stoppedTask;
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(SingleConnectionServerTransport));
            }

            if (_started == 0)
            {
                throw new InvalidOperationException("Transport has not been started.");
            }

            ct.ThrowIfCancellationRequested();

            if (_accepted == 0)
            {
                _accepted = 1;
                return _connection;
            }

            stoppedTask = _stopped.Task;
        }

        ct.ThrowIfCancellationRequested();

        if (ct.CanBeCanceled)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(static state =>
                ((TaskCompletionSource<bool>)state!).TrySetResult(true), cancelled))
            {
                var completed = await Task.WhenAny(stoppedTask, cancelled.Task).ConfigureAwait(false);
                if (ReferenceEquals(completed, cancelled.Task))
                {
                    ct.ThrowIfCancellationRequested();
                }
            }
        }
        else
        {
            await stoppedTask.ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        // Associate the cancellation with the caller's token even when StopAsync (not the token) released
        // the parked accept, so token-scoped catch filters observe it — matching Tcp/NamedPipe transports.
        throw new OperationCanceledException(ct);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        TaskCompletionSource<bool> stopped;
        lock (_sync)
        {
            if (_started == 0)
            {
                return Task.CompletedTask;
            }

            _started = 0;
            stopped = _stopped;
        }

        stopped.TrySetResult(true);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        TaskCompletionSource<bool> stopped;
        lock (_sync)
        {
            _started = 0;
            stopped = _stopped;
        }

        stopped.TrySetResult(true);

        if (_ownsConnection)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static TaskCompletionSource<bool> CreateStoppedSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
