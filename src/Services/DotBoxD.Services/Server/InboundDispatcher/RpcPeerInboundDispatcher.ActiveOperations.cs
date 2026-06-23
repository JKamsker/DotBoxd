namespace DotBoxD.Services.Server;

internal sealed partial class RpcPeerInboundDispatcher
{
    private bool TryEnterActiveRequest() =>
        TryEnterActiveOperation(ref _activeRequestCount, ref _activeRequestsDrained);

    private bool TryEnterActiveStream() =>
        TryEnterActiveOperation(ref _activeStreamCount, ref _activeStreamsDrained);

    private bool TryEnterActiveOperation(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Volatile.Read(ref _stopped) != 0)
        {
            return false;
        }

        Interlocked.Increment(ref count);
        if (Volatile.Read(ref _stopped) == 0)
        {
            return true;
        }

        CompleteActiveOperation(ref count, ref drained);
        return false;
    }

    private Task WaitForActiveRequestsAsync() =>
        WaitForActiveOperationsAsync(ref _activeRequestCount, ref _activeRequestsDrained);

    private Task WaitForActiveStreamsAsync() =>
        WaitForActiveOperationsAsync(ref _activeStreamCount, ref _activeStreamsDrained);

    private static Task WaitForActiveOperationsAsync(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Volatile.Read(ref count) == 0)
        {
            return Task.CompletedTask;
        }

        var signal = Volatile.Read(ref drained);
        if (signal is null)
        {
            var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = Interlocked.CompareExchange(ref drained, created, null) ?? created;
        }

        if (Volatile.Read(ref count) == 0)
        {
            signal.TrySetResult(true);
        }

        return signal.Task;
    }

    private void CompleteActiveRequest() =>
        CompleteActiveOperation(ref _activeRequestCount, ref _activeRequestsDrained);

    private void CompleteActiveStream() =>
        CompleteActiveOperation(ref _activeStreamCount, ref _activeStreamsDrained);

    private static void CompleteActiveOperation(
        ref int count,
        ref TaskCompletionSource<bool>? drained)
    {
        if (Interlocked.Decrement(ref count) == 0)
        {
            Volatile.Read(ref drained)?.TrySetResult(true);
        }
    }
}
