namespace ShaRPC.Core;

internal sealed class RpcPeerActiveInboundRequests
{
    private readonly object _gate = new();
    private readonly Dictionary<int, CancellationTokenSource?> _requests = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    public bool TryAdd(int messageId, CancellationTokenSource? requestCts)
    {
        lock (_gate)
        {
            if (_requests.ContainsKey(messageId))
            {
                return false;
            }

            _requests.Add(messageId, requestCts);
            return true;
        }
    }

    public bool TryGet(int messageId, out CancellationTokenSource? requestCts)
    {
        lock (_gate)
        {
            return _requests.TryGetValue(messageId, out requestCts);
        }
    }

    public void Cancel(int messageId)
    {
        if (TryGet(messageId, out var requestCts) &&
            requestCts is not null)
        {
            SafeCancel(requestCts);
        }
    }

    public CancellationTokenSource[] Snapshot()
    {
        lock (_gate)
        {
            if (_requests.Count == 0)
            {
                return Array.Empty<CancellationTokenSource>();
            }

            var snapshot = new List<CancellationTokenSource>(_requests.Count);
            foreach (var requestCts in _requests.Values)
            {
                if (requestCts is not null)
                {
                    snapshot.Add(requestCts);
                }
            }

            return snapshot.Count == 0 ? Array.Empty<CancellationTokenSource>() : snapshot.ToArray();
        }
    }

    public void CancelAll()
    {
        foreach (var requestCts in Snapshot())
        {
            SafeCancel(requestCts);
        }
    }

    public void Remove(int messageId, CancellationTokenSource? requestCts)
    {
        lock (_gate)
        {
            if (_requests.TryGetValue(messageId, out var current) &&
                ReferenceEquals(current, requestCts))
            {
                _requests.Remove(messageId);
            }
        }
    }

    private static void SafeCancel(CancellationTokenSource cts)
    {
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The request completed while the connection was closing.
        }
    }
}
