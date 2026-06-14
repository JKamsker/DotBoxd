using ShaRPC.Core.Attributes;

namespace Shared;

/// <summary>
/// Game service interface defining the RPC contract.
/// </summary>
[ShaRpcService]
public interface IGameService
{
    Task<PlayerState> GetPlayerStateAsync(PlayerId playerId, CancellationToken ct = default);

    Task<ActionResult> MovePlayerAsync(MoveRequest request, CancellationToken ct = default);

    Task<ActionResult> PerformActionAsync(ActionRequest request, CancellationToken ct = default);

    Task<ServerStatus> GetServerStatusAsync(CancellationToken ct = default);

    Task<PlayerState> RegisterPlayerAsync(string playerName, CancellationToken ct = default);
}
