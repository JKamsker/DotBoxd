namespace DotBoxD.Kernels.Game.Server.Ipc;

internal sealed class RelayRateLimiter
{
    private readonly int _limit;
    private readonly object _gate = new();
    private int _remaining;
    private int _windowTick = Environment.TickCount;

    public RelayRateLimiter(int limit)
    {
        _limit = limit;
        _remaining = limit;
    }

    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = Environment.TickCount;
            if (unchecked(now - _windowTick) > 1_000)
            {
                _windowTick = now;
                _remaining = _limit;
            }

            if (_remaining <= 0)
            {
                return false;
            }

            _remaining--;
            return true;
        }
    }
}
