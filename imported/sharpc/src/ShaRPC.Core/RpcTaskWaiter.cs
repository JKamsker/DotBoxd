namespace ShaRPC.Core;

internal static class RpcTaskWaiter
{
    public static Task WaitAsync(Task task, CancellationToken ct)
    {
        if (!ct.CanBeCanceled || task.IsCompleted)
        {
            return task;
        }

        return WaitCoreAsync(task, ct);
    }

    private static async Task WaitCoreAsync(Task task, CancellationToken ct)
    {
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), canceled))
        {
            if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
            {
                _ = task.ContinueWith(
                    static completed => RpcDiagnostics.Report("Orphaned task faulted after cancellation", completed.Exception!),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new OperationCanceledException(ct);
            }
        }

        await task.ConfigureAwait(false);
    }
}
