namespace ShaRPC.Benchmarks;

internal sealed class ZeroAllocGameService
{
    private PlayerStateValue _player;
    private int _registered;
    private long _lastHeartbeat;

    public PlayerStateValue Register(RegisterPlayerRequest request)
    {
        _registered = 1;
        _player = new PlayerStateValue(1, request.NameToken, 1, 100, 100, 0, 0, 0);
        return _player;
    }

    public ActionResultValue TryGetPlayerState(GetPlayerStateRequest request, out PlayerStateValue player)
    {
        if (_registered == 0 || request.PlayerId != _player.PlayerId)
        {
            player = default;
            return new ActionResultValue(success: 0, code: 404);
        }

        player = _player;
        return new ActionResultValue(success: 1, code: 0);
    }

    public ActionResultValue Move(MovePlayerRequest request)
    {
        if (_registered == 0 || request.PlayerId != _player.PlayerId)
        {
            return new ActionResultValue(success: 0, code: 404);
        }

        _player = _player.WithPosition(request.X, request.Y, request.Z);
        return new ActionResultValue(success: 1, code: 0);
    }

    public ActionResultValue PerformAction(PerformActionRequest request)
    {
        if (_registered == 0 || request.PlayerId != _player.PlayerId)
        {
            return new ActionResultValue(success: 0, code: 404);
        }

        return new ActionResultValue(success: 1, code: request.ActionToken ^ request.TargetToken);
    }

    public void Heartbeat(HeartbeatRequest request)
    {
        if (_registered != 0 && request.PlayerId == _player.PlayerId)
        {
            _lastHeartbeat = request.Tick;
        }
    }

    public ServerStatusValue GetStatus() =>
        new(_registered, _lastHeartbeat, versionMajor: 1, versionMinor: 0);
}
