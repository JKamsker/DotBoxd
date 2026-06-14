using System.Threading.Channels;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Transport;

namespace ShaRPC.Tests;

internal sealed class ScriptedConnection : IRpcChannel
{
    private readonly Channel<Payload> _inbound = Channel.CreateUnbounded<Payload>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly List<(int Count, TaskCompletionSource<bool> Completion)> _waiters = new();
    private readonly List<(int Attempt, TaskCompletionSource<bool> Completion)> _attemptWaiters = new();
    private int _disposed;
    private int _receiveAttempts;
    private int _receiveCount;

    public bool IsConnected => Volatile.Read(ref _disposed) == 0;

    public string RemoteEndpoint => "scripted://remote";

    public int ReceiveCount => Volatile.Read(ref _receiveCount);

    public void Enqueue(Payload frame) => _inbound.Writer.TryWrite(frame);

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async Task<Payload> ReceiveAsync(CancellationToken ct = default)
    {
        CompleteAttemptWaiters(Interlocked.Increment(ref _receiveAttempts));
        try
        {
            var frame = await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);
            CompleteWaiters(Interlocked.Increment(ref _receiveCount));
            return frame;
        }
        catch (ChannelClosedException)
        {
            return Payload.Empty;
        }
    }

    public Task WaitForReceiveCountAsync(int count, TimeSpan timeout)
    {
        if (ReceiveCount >= count)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waiters)
        {
            if (ReceiveCount >= count)
            {
                return Task.CompletedTask;
            }

            _waiters.Add((count, completion));
        }

        return completion.Task.WaitAsync(timeout);
    }

    public Task WaitForReceiveAttemptAsync(int attempt, TimeSpan timeout)
    {
        if (Volatile.Read(ref _receiveAttempts) >= attempt)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_attemptWaiters)
        {
            if (Volatile.Read(ref _receiveAttempts) >= attempt)
            {
                return Task.CompletedTask;
            }

            _attemptWaiters.Add((attempt, completion));
        }

        return completion.Task.WaitAsync(timeout);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return default;
        }

        _inbound.Writer.TryComplete();
        while (_inbound.Reader.TryRead(out var frame))
        {
            frame.Dispose();
        }

        return default;
    }

    private void CompleteWaiters(int count)
    {
        List<TaskCompletionSource<bool>>? completed = null;
        lock (_waiters)
        {
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (count < _waiters[i].Count)
                {
                    continue;
                }

                completed ??= new List<TaskCompletionSource<bool>>();
                completed.Add(_waiters[i].Completion);
                _waiters.RemoveAt(i);
            }
        }

        Complete(completed);
    }

    private void CompleteAttemptWaiters(int attempt)
    {
        List<TaskCompletionSource<bool>>? completed = null;
        lock (_attemptWaiters)
        {
            for (var i = _attemptWaiters.Count - 1; i >= 0; i--)
            {
                if (attempt < _attemptWaiters[i].Attempt)
                {
                    continue;
                }

                completed ??= new List<TaskCompletionSource<bool>>();
                completed.Add(_attemptWaiters[i].Completion);
                _attemptWaiters.RemoveAt(i);
            }
        }

        Complete(completed);
    }

    private static void Complete(List<TaskCompletionSource<bool>>? completed)
    {
        if (completed is null)
        {
            return;
        }

        foreach (var completion in completed)
        {
            completion.TrySetResult(true);
        }
    }
}
