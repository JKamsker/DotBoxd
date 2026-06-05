using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;
using ShaRPC.Core.Streaming;

namespace ShaRPC.Core.Client;

internal sealed class ShaRpcPendingRequests
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ReceivedResponse>> _requests = new();

    public int Count => _requests.Count;

    public bool TryAdd(int messageId, out TaskCompletionSource<ReceivedResponse> tcs)
    {
        tcs = new TaskCompletionSource<ReceivedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_requests.TryAdd(messageId, tcs))
        {
            return true;
        }

        tcs = null!;
        return false;
    }

    public void Remove(int messageId, Task<ReceivedResponse> task, bool consumed)
    {
        _requests.TryRemove(messageId, out _);
        if (!consumed)
        {
            ReceivedResponse.DisposeWhenAvailable(task);
        }
    }

    public bool TryTake(int messageId, out TaskCompletionSource<ReceivedResponse> tcs) =>
        _requests.TryRemove(messageId, out tcs!);

    public bool TryComplete(
        int messageId,
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        Payload frame,
        RpcStreamReceiver? stream)
    {
        if (!_requests.TryRemove(messageId, out var tcs))
        {
            return false;
        }

        var received = new ReceivedResponse(response, payload, frame, stream);
        if (!tcs.TrySetResult(received))
        {
            received.Dispose();
        }

        return true;
    }

    public bool TryFail(int messageId, Exception error)
    {
        if (!_requests.TryRemove(messageId, out var tcs))
        {
            return false;
        }

        tcs.TrySetException(error);
        return true;
    }

    /// <summary>
    /// Atomically removes the pending request and cancels it. Returns <see langword="false"/> when the
    /// entry was already removed (for example, a response completed it first), making the caller a
    /// no-op. This lets a timeout and a response race on a single removal so a delivered response is
    /// never discarded as a spurious cancellation.
    /// </summary>
    public bool TryCancel(int messageId)
    {
        if (!_requests.TryRemove(messageId, out var tcs))
        {
            return false;
        }

        tcs.TrySetCanceled();
        return true;
    }

    public void FailAll(Exception error)
    {
        // Remove by exact key+value, not key alone: a teardown racing a brand-new request that reused a
        // wrapped-around message id (the counter is a 32-bit Interlocked.Increment) must fail only the
        // request captured in the snapshot, never the new request's different completion source. The
        // ICollection<KeyValuePair> remover matches both key and value atomically (netstandard2.1 has no
        // public ConcurrentDictionary.TryRemove(KeyValuePair) overload).
        var entries = (ICollection<KeyValuePair<int, TaskCompletionSource<ReceivedResponse>>>)_requests;
        foreach (var pair in _requests.ToArray())
        {
            if (entries.Remove(pair))
            {
                pair.Value.TrySetException(error);
            }
        }
    }
}
