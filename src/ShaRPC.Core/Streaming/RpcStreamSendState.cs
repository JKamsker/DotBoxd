namespace ShaRPC.Core.Streaming;

internal sealed class RpcStreamSendState : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly SemaphoreSlim _credits = new(0);
    private int _disposed;

    public RpcStreamSendState(int streamId, CancellationToken ownerToken)
    {
        StreamId = streamId;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
    }

    public int StreamId { get; }

    public CancellationToken Token => _cts.Token;

    public bool IsCancellationRequested => _cts.IsCancellationRequested;

    public Task WaitForCreditAsync(CancellationToken ct) =>
        _credits.WaitAsync(ct);

    public void AddCredit(int count)
    {
        if (count <= 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _credits.Release(count);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SemaphoreFullException)
        {
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
}
