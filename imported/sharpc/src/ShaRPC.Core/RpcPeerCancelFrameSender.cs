using ShaRPC.Core.Protocol;

namespace ShaRPC.Core;

internal sealed class RpcPeerCancelFrameSender
{
    private const int MaxInFlightFrames = 16;

    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, Task> _sendAsync;
    private readonly SemaphoreSlim _slots = new(MaxInFlightFrames, MaxInFlightFrames);
    private readonly HashSet<Task> _tasks = new();
    private readonly object _lock = new();
    private bool _closed;

    public RpcPeerCancelFrameSender(Func<ReadOnlyMemory<byte>, CancellationToken, Task> sendAsync) =>
        _sendAsync = sendAsync;

    public void TrySend(int messageId)
    {
        Task task;
        lock (_lock)
        {
            if (_closed || !_slots.Wait(0))
            {
                return;
            }

            task = SendAsync(messageId);
            _tasks.Add(task);
        }

        _ = task.ContinueWith(
            static (completed, state) => ((RpcPeerCancelFrameSender)state!).Complete(completed),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async Task StopAsync()
    {
        Task[] tasks;
        lock (_lock)
        {
            _closed = true;
            tasks = _tasks.ToArray();
        }

        if (tasks.Length != 0)
        {
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // Individual cancel frames are best-effort and observe their own failures.
            }
        }

        _slots.Dispose();
    }

    private void Complete(Task task)
    {
        lock (_lock)
        {
            _tasks.Remove(task);
            try
            {
                _slots.Release();
            }
            catch (ObjectDisposedException)
            {
                // StopAsync already disposed _slots.
            }
        }
    }

    private async Task SendAsync(int messageId)
    {
        try
        {
            using var frame = MessageFramer.FrameToPayload(
                messageId,
                MessageType.Cancel,
                ReadOnlySpan<byte>.Empty);
            await _sendAsync(frame.Memory, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Outbound cancel frame send failed", ex);
        }
    }
}
