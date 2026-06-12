using ShaRPC.Core.Attributes;

namespace Shared;

/// <summary>
/// ValueTask-returning variant of <see cref="IGameService"/> used by allocation benchmarks.
/// </summary>
[ShaRpcService]
public interface IValueTaskGameService
{
    ValueTask<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default);

    ValueTask<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default);

    ValueTask<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default);

    ValueTask<ServerStatus> GetServerStatusAsync(CancellationToken ct = default);

    ValueTask<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default);
}
