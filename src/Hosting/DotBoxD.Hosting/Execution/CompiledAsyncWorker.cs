using System.Runtime.ExceptionServices;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Hosting.Execution;

internal sealed class CompiledAsyncWorker(Func<SandboxExecutionResult> execute)
{
    private readonly TaskCompletionSource<SandboxExecutionResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static ValueTask<SandboxExecutionResult> RunAsync(Func<SandboxExecutionResult> execute)
    {
        var worker = new CompiledAsyncWorker(execute);
        worker.Start();
        return new ValueTask<SandboxExecutionResult>(worker._completion.Task);
    }

    public static SandboxExecutionResult RunInline(Func<SandboxExecutionResult> execute)
    {
        using var pump = new CompiledAwaitPump();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(pump);
        using var scope = CompiledBindingDispatcher.InstallAwaitPump(pump);
        try
        {
            return execute();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private void Start()
    {
        var thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DotBoxD compiled async worker"
        };
        thread.Start();
    }

    private void Run()
    {
        try
        {
            _completion.SetResult(RunInline(execute));
        }
        catch (Exception ex)
        {
            _completion.SetException(ex);
        }
    }

    private sealed class CompiledAwaitPump : SynchronizationContext, ICompiledAwaitPump, IDisposable
    {
        private readonly Queue<WorkItem> _queue = new();
        private readonly AutoResetEvent _signal = new(false);
        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
        private bool _disposed;

        public SandboxValue RunToCompletion(ValueTask<SandboxValue> pending)
        {
            var task = pending.AsTask();
            if (!task.IsCompleted)
            {
                _ = task.ContinueWith(
                    static (_, state) => ((CompiledAwaitPump)state!).Signal(),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            while (!task.IsCompleted)
            {
                if (TryDequeue(out var item))
                {
                    Invoke(item);
                    continue;
                }

                _signal.WaitOne();
            }

            return task.GetAwaiter().GetResult();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            Enqueue(new WorkItem(d, state, null));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (Environment.CurrentManagedThreadId == _ownerThreadId)
            {
                d(state);
                return;
            }

            using var waitState = new SendWaitState(this);
            Enqueue(new WorkItem(d, state, waitState));
            waitState.Wait();
        }

        public override SynchronizationContext CreateCopy() => this;

        public void Dispose()
        {
            var pending = new List<WorkItem>();
            lock (_queue)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                while (_queue.Count > 0)
                {
                    pending.Add(_queue.Dequeue());
                }
            }

            foreach (var item in pending)
            {
                item.WaitState?.CancelDisposed();
            }

            _signal.Dispose();
        }

        private void Signal()
        {
            try
            {
                _signal.Set();
            }
            catch (ObjectDisposedException)
            {
                ObjectDisposedException.ThrowIf(!_disposed, this);
            }
        }

        private void Enqueue(WorkItem item)
        {
            lock (_queue)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _queue.Enqueue(item);
                Signal();
            }
        }

        private bool TryDequeue(out WorkItem item)
        {
            lock (_queue)
            {
                if (_queue.Count > 0)
                {
                    item = _queue.Dequeue();
                    return true;
                }
            }

            item = default;
            return false;
        }

        private static void Invoke(WorkItem item)
        {
            if (item.WaitState is null)
            {
                item.Callback(item.State);
                return;
            }

            ExceptionDispatchInfo? error = null;
            try
            {
                item.Callback(item.State);
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                item.WaitState.Complete(error);
            }
        }

        private sealed class SendWaitState(CompiledAwaitPump owner) : IDisposable
        {
            private readonly ManualResetEventSlim _completed = new();
            private ExceptionDispatchInfo? _error;

            public void Wait()
            {
                _completed.Wait();
                _error?.Throw();
            }

            public void Complete(ExceptionDispatchInfo? error)
            {
                _error = error;
                _completed.Set();
            }

            public void CancelDisposed()
                => Complete(ExceptionDispatchInfo.Capture(new ObjectDisposedException(owner.GetType().FullName)));

            public void Dispose() => _completed.Dispose();
        }

        private readonly record struct WorkItem(
            SendOrPostCallback Callback,
            object? State,
            SendWaitState? WaitState);
    }
}
