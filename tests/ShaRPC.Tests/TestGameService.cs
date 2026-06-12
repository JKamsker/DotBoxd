using Shared;

namespace ShaRPC.Tests;

/// <summary>
/// In-memory <see cref="IGameService"/> implementation shared by the transport integration tests
/// (TCP and in-memory pipe). Keeps a small player registry so move/action calls have observable
/// state to assert against.
/// </summary>
internal sealed class TestGameService : IGameService
{
    private readonly Dictionary<string, PlayerState> _players = new();
    private int _idCounter;

    public Task<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default)
    {
        if (_players.TryGetValue(playerId.Id, out var state))
        {
            return Task.FromResult(state);
        }
        throw new KeyNotFoundException($"Player {playerId.Id} not found");
    }

    public Task<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default)
    {
        if (request.PlayerId is { } playerId &&
            _players.TryGetValue(playerId, out var state))
        {
            state.PositionX = request.X;
            state.PositionY = request.Y;
            state.PositionZ = request.Z;
            return Task.FromResult(new ActionResult { Success = true, Message = "Moved" });
        }
        return Task.FromResult(new ActionResult { Success = false, Message = "Not found" });
    }

    public Task<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default)
    {
        if (!_players.ContainsKey(request.PlayerId))
        {
            return Task.FromResult(new ActionResult { Success = false, Message = "Not found" });
        }
        return Task.FromResult(new ActionResult { Success = true, Message = $"Action {request.ActionType} performed" });
    }

    public Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new ServerStatus
        {
            PlayerCount = _players.Count,
            ServerTime = DateTime.UtcNow.ToString("O"),
            Version = "1.0.0-test"
        });
    }

    public Task<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default)
    {
        var id = $"test_player_{Interlocked.Increment(ref _idCounter)}";
        var state = new PlayerState
        {
            PlayerId = id,
            Name = playerName,
            Level = 1,
            Health = 100,
            MaxHealth = 100,
            PositionX = 0,
            PositionY = 0,
            PositionZ = 0
        };
        _players[id] = state;
        return Task.FromResult(state);
    }
}
