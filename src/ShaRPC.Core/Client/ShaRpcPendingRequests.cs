using System.Collections.Concurrent;
using ShaRPC.Core.Buffers;
using ShaRPC.Core.Protocol;

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

    public bool TryComplete(
        int messageId,
        RpcResponse response,
        ReadOnlyMemory<byte> payload,
        Payload frame)
    {
        if (!_requests.TryRemove(messageId, out var tcs))
        {
            return false;
        }

        var received = new ReceivedResponse(response, payload, frame);
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

    public void FailAll(Exception error)
    {
        foreach (var key in _requests.Keys)
        {
            if (_requests.TryRemove(key, out var tcs))
            {
                tcs.TrySetException(error);
            }
        }
    }

    public void CancelAll()
    {
        foreach (var key in _requests.Keys)
        {
            if (_requests.TryRemove(key, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }
    }
}
