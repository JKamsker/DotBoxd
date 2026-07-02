namespace DotBoxD.Services.Streaming.Core;

internal sealed class RpcStreamSendState : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly CancellationToken _token;
    private readonly SemaphoreSlim _credits = new(0);
    private int _availableCredits;
    private int _disposed;

    public RpcStreamSendState(int streamId, CancellationToken ownerToken)
    {
        StreamId = streamId;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        _token = _cts.Token;
    }

    public int StreamId { get; }

    public CancellationToken Token => _token;

    public bool IsCancellationRequested => _token.IsCancellationRequested;

    public async Task WaitForCreditAsync(CancellationToken ct)
    {
        await _credits.WaitAsync(ct).ConfigureAwait(false);
        Interlocked.Decrement(ref _availableCredits);
    }

    public bool AddCredit(int count)
    {
        if (count <= 0)
        {
            return true;
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return true;
        }

        if (!TryReserveCredits(count))
        {
            return false;
        }

        try
        {
            _credits.Release(count);
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    private bool TryReserveCredits(int count)
    {
        if (count > RpcStreamManager.WindowSize)
        {
            return false;
        }

        while (true)
        {
            var current = Volatile.Read(ref _availableCredits);
            if (current > RpcStreamManager.WindowSize - count)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _availableCredits, current + count, current) == current)
            {
                return true;
            }
        }
    }

    public void Cancel()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Cancel();
        _credits.Dispose();
        _cts.Dispose();
    }

    public void DisposeAfterCompletion()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _credits.Dispose();
        _cts.Dispose();
    }
}
