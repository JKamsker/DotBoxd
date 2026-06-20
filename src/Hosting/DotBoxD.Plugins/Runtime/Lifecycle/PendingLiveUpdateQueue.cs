namespace DotBoxD.Plugins.Runtime.Lifecycle;

internal sealed class PendingLiveUpdateQueue
{
    private readonly object _gate = new();
    private readonly List<Task> _pending = [];

    public Exception? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    private Exception? _lastError;

    public void Enqueue(Action update)
    {
        var task = Task.Run(() =>
        {
            try
            {
                update();
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _lastError = ex;
                }

                throw;
            }
        });
        lock (_gate)
        {
            _pending.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                if (!completed.IsCompletedSuccessfully)
                {
                    return;
                }

                lock (_gate)
                {
                    _pending.Remove(completed);
                }
            },
            TaskScheduler.Default);
    }

    public void ClearError()
    {
        lock (_gate)
        {
            _lastError = null;
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        Task[] pending;
        lock (_gate)
        {
            _pending.RemoveAll(task => task.IsCompletedSuccessfully);
            pending = _pending.ToArray();
        }

        try
        {
            await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || LastError is not null)
        {
            var failure = LastError ?? ex;
            lock (_gate)
            {
                _pending.RemoveAll(task => task.IsCompleted);
                _lastError = failure;
            }

            throw new InvalidOperationException("A fire-and-forget live setting update failed.", failure);
        }

        lock (_gate)
        {
            _pending.RemoveAll(task => task.IsCompleted);
            _lastError = null;
        }
    }
}
