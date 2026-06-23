namespace DotBoxD.Services.Peer.Inbound;

internal sealed class RpcPeerInboundByteBudget
{
    private readonly long _maxInboundBytes;
    private readonly object _gate = new();
    private long _inFlightBytes;
    private TaskCompletionSource<bool>? _available;

    public RpcPeerInboundByteBudget(long? maxInboundBytes) =>
        _maxInboundBytes = maxInboundBytes ?? long.MaxValue;

    public bool TryAdmit(long bytes)
    {
        if (_maxInboundBytes == long.MaxValue)
        {
            return true;
        }

        lock (_gate)
        {
            if (HasCapacity(bytes))
            {
                _inFlightBytes += bytes;
                return true;
            }

            return false;
        }
    }

    public async ValueTask AdmitAsync(long bytes, CancellationToken ct)
    {
        if (_maxInboundBytes == long.MaxValue)
        {
            return;
        }

        // The peer read loop is the only writer, so at most one waiter exists at a time.
        while (true)
        {
            Task wait;
            lock (_gate)
            {
                if (HasCapacity(bytes))
                {
                    _inFlightBytes += bytes;
                    return;
                }

                _available ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = _available.Task;
            }

            await RpcTaskWaiter.WaitAsync(wait, ct).ConfigureAwait(false);
        }
    }

    public void Release(long bytes)
    {
        if (_maxInboundBytes == long.MaxValue)
        {
            return;
        }

        TaskCompletionSource<bool>? signal;
        lock (_gate)
        {
            _inFlightBytes -= bytes;
            signal = _available;
            _available = null;
        }

        signal?.TrySetResult(true);
    }

    private bool HasCapacity(long bytes) =>
        _inFlightBytes == 0 || _inFlightBytes + bytes <= _maxInboundBytes;
}

internal sealed class RpcPeerInboundInFlightTracker
{
    private TaskCompletionSource<bool>? _drained;
    private int _count;

    public void Add() => Interlocked.Increment(ref _count);

    public Task WaitAsync()
    {
        if (Volatile.Read(ref _count) == 0)
        {
            return Task.CompletedTask;
        }

        var signal = Volatile.Read(ref _drained);
        if (signal is null)
        {
            var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = Interlocked.CompareExchange(ref _drained, created, null) ?? created;
        }

        if (Volatile.Read(ref _count) == 0)
        {
            signal.TrySetResult(true);
        }

        return signal.Task;
    }

    public void Complete()
    {
        if (Interlocked.Decrement(ref _count) == 0)
        {
            Volatile.Read(ref _drained)?.TrySetResult(true);
        }
    }
}
