using System.Collections.Concurrent;
using Shared;

namespace Server;

/// <summary>
/// Server-side implementation of the game service.
/// </summary>
public sealed class GameService : IGameService
{
    private readonly ConcurrentDictionary<string, PlayerState> _players = new();
    private int _playerIdCounter = 0;

    public Task<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default)
    {
        if (_players.TryGetValue(playerId.Id, out var state))
        {
            return Task.FromResult(state);
        }

        throw new KeyNotFoundException($"Player '{playerId.Id}' not found.");
    }

    public Task<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default)
    {
        var playerId = request.PlayerId;
        if (string.IsNullOrEmpty(playerId) ||
            !_players.TryGetValue(playerId, out var currentState))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Player '{playerId ?? "<missing>"}' not found."
            });
        }

        currentState.PositionX = request.X;
        currentState.PositionY = request.Y;
        currentState.PositionZ = request.Z;

        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Player moved to ({request.X}, {request.Y}, {request.Z})"
        });
    }

    public Task<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default)
    {
        if (!_players.ContainsKey(request.PlayerId))
        {
            return Task.FromResult(new ActionResult
            {
                Success = false,
                Message = $"Player '{request.PlayerId}' not found."
            });
        }

        // Simulate action handling
        Console.WriteLine($"Player {request.PlayerId} performed action: {request.ActionType} (target: {request.TargetId ?? "none"})");

        return Task.FromResult(new ActionResult
        {
            Success = true,
            Message = $"Action '{request.ActionType}' performed successfully."
        });
    }

    public Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new ServerStatus
        {
            PlayerCount = _players.Count,
            ServerTime = DateTime.UtcNow.ToString("O"),
            Version = "1.0.0"
        });
    }

    public Task<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default)
    {
        var playerId = $"player_{Interlocked.Increment(ref _playerIdCounter)}";

        var state = new PlayerState
        {
            PlayerId = playerId,
            Name = playerName,
            Level = 1,
            Health = 100,
            MaxHealth = 100,
            PositionX = 0,
            PositionY = 0,
            PositionZ = 0
        };

        _players[playerId] = state;
        Console.WriteLine($"Registered new player: {playerName} ({playerId})");

        return Task.FromResult(state);
    }
}
