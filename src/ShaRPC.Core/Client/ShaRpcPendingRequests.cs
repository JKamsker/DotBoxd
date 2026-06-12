using System.Diagnostics;

namespace ShaRPC.Core.Client;

internal sealed class ShaRpcPendingRequests : IDisposable
{
    private readonly object _requestsGate = new();
    private readonly object _timeoutGate = new();
    private readonly Dictionary<int, IPendingResponse> _requests = new();
    private readonly Timer _timeoutTimer;
    private long _nextTimeoutTimestamp = long.MaxValue;
    private int _disposed;

    public ShaRpcPendingRequests()
    {
        _timeoutTimer = new Timer(
            static state => ((ShaRpcPendingRequests)state!).CancelExpired(),
            this,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public int Count
    {
        get { lock (_requestsGate) { return _requests.Count; } }
    }

    public bool TryAdd(int messageId, out PendingReceivedResponse pending) =>
        TryAddCore(messageId, new PendingReceivedResponse(this, messageId), out pending);

    public bool TryAddUnary<TResponse>(
        int messageId,
        bool captureCallerCancellation,
        bool captureTimeoutTarget,
        string service,
        string method,
        out PendingUnaryResponse<TResponse> pending)
    {
        var candidate = captureTimeoutTarget
            ? new PendingUnaryResponseWithTimeout<TResponse>(this, messageId, service, method)
            : captureCallerCancellation
                ? new CancellablePendingUnaryResponse<TResponse>(this, messageId)
                : new PendingUnaryResponse<TResponse>(messageId);

        return TryAddCore(messageId, candidate, out pending);
    }

    public bool TryAddValueTaskUnary<TResponse>(
        int messageId,
        out PendingValueTaskUnaryResponse<TResponse> pending)
    {
        var candidate = PendingValueTaskUnaryResponse<TResponse>.Rent(messageId);
        var added = TryAddCore(messageId, candidate, out pending);
        if (!added) candidate.Abandon();
        return added;
    }

    private bool TryAddCore<TPending>(int messageId, TPending candidate, out TPending pending)
        where TPending : IPendingResponse
    {
        lock (_requestsGate)
        {
            if (_requests.TryAdd(messageId, candidate))
            {
                pending = candidate;
                return true;
            }
        }

        pending = default!;
        return false;
    }

    public void Remove(int messageId, IPendingResponse pending, bool consumed)
    {
        TryRemove(messageId, pending);
        if (!consumed)
        {
            pending.DisposeResultWhenAvailable();
        }
    }

    public bool TryTake(int messageId, out IPendingResponse pending)
    {
        lock (_requestsGate)
        {
            if (!_requests.TryGetValue(messageId, out pending!))
            {
                return false;
            }

            _requests.Remove(messageId);
            return true;
        }
    }

    public void StartTimeout(IPendingResponse pending, TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        var timeoutTicks = MillisecondsToStopwatchTicks((long)Math.Ceiling(timeout.TotalMilliseconds));
        var deadline = Stopwatch.GetTimestamp() + timeoutTicks;
        pending.SetTimeoutDeadline(deadline);
        ScheduleTimeout(deadline);
    }

    public bool TryFail(int messageId, Exception error)
    {
        if (!TryTake(messageId, out var completion))
        {
            return false;
        }

        completion.SetError(error);
        return true;
    }

    /// <summary>
    /// Atomically removes the pending request and cancels it. Returns <see langword="false"/> when the
    /// entry was already removed (for example, a response completed it first), making the caller a
    /// no-op. This lets a timeout and a response race on a single removal so a delivered response is
    /// never discarded as a spurious cancellation.
    /// </summary>
    public bool TryCancel(
        int messageId,
        IPendingResponse pending,
        PendingCancellationKind kind)
    {
        if (!TryRemove(messageId, pending))
        {
            return false;
        }

        pending.TrySetCanceled(kind);
        return true;
    }

    public void FailAll(Exception error)
    {
        IPendingResponse[] pending;
        lock (_requestsGate)
        {
            if (_requests.Count == 0)
            {
                return;
            }

            pending = new IPendingResponse[_requests.Count];
            _requests.Values.CopyTo(pending, 0);
            _requests.Clear();
        }

        foreach (var request in pending)
        {
            request.SetError(error);
        }
    }

    public void Dispose()
    {
        lock (_timeoutGate)
        {
            if (_disposed != 0)
            {
                return;
            }

            _disposed = 1;
            _timeoutTimer.Dispose();
        }
    }

    private void ScheduleTimeout(long deadline)
    {
        lock (_timeoutGate)
        {
            if (_disposed != 0 || deadline >= _nextTimeoutTimestamp)
            {
                return;
            }

            _nextTimeoutTimestamp = deadline;
            ScheduleTimerLocked();
        }
    }

    private void CancelExpired()
    {
        lock (_timeoutGate)
        {
            if (_disposed != 0)
            {
                return;
            }

            _nextTimeoutTimestamp = long.MaxValue;
        }

        var now = Stopwatch.GetTimestamp();
        var next = long.MaxValue;
        List<IPendingResponse>? expired = null;
        lock (_requestsGate)
        {
            foreach (var pair in _requests)
            {
                var deadline = pair.Value.TimeoutDeadline;
                if (deadline == long.MaxValue)
                {
                    continue;
                }

                if (deadline <= now)
                {
                    expired ??= new List<IPendingResponse>();
                    expired.Add(pair.Value);
                }
                else if (deadline < next)
                {
                    next = deadline;
                }
            }

            if (expired is not null)
            {
                for (var i = 0; i < expired.Count; i++)
                {
                    var pending = expired[i];
                    TryRemoveCore(pending.MessageId, pending);
                }
            }
        }

        if (expired is not null)
        {
            for (var i = 0; i < expired.Count; i++)
            {
                expired[i].TrySetCanceled(PendingCancellationKind.Timeout);
            }
        }

        lock (_timeoutGate)
        {
            if (_disposed != 0)
            {
                return;
            }

            if (next < _nextTimeoutTimestamp)
            {
                _nextTimeoutTimestamp = next;
            }

            ScheduleTimerLocked();
        }
    }

    private void ScheduleTimerLocked()
    {
        if (_disposed != 0) return;

        if (_nextTimeoutTimestamp == long.MaxValue)
        {
            _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return;
        }

        var remainingTicks = Math.Max(0, _nextTimeoutTimestamp - Stopwatch.GetTimestamp());
        var dueMilliseconds = Math.Min(
            int.MaxValue,
            Math.Max(1, StopwatchTicksToMilliseconds(remainingTicks)));
        _timeoutTimer.Change(dueMilliseconds, Timeout.Infinite);
    }

    private static long MillisecondsToStopwatchTicks(long milliseconds) =>
        checked(milliseconds * Stopwatch.Frequency / 1000);

    private static long StopwatchTicksToMilliseconds(long ticks) =>
        ticks * 1000 / Stopwatch.Frequency;

    private bool TryRemove(int messageId, IPendingResponse pending)
    {
        lock (_requestsGate) { return TryRemoveCore(messageId, pending); }
    }

    private bool TryRemoveCore(int messageId, IPendingResponse pending)
    {
        if (!_requests.TryGetValue(messageId, out var current) ||
            !ReferenceEquals(current, pending))
        {
            return false;
        }

        _requests.Remove(messageId);
        return true;
    }
}
