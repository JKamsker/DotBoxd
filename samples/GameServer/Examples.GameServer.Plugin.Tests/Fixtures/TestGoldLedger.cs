using DotBoxD.Kernels.Game.Server.Abstractions;

namespace DotBoxD.Kernels.Game.Plugin.Tests;

internal sealed class TestGoldLedger : IGoldLedger
{
    public static TestGoldLedger Instance { get; } = new();

    public ValueTask<int> GetBalanceAsync(string entityId) => ValueTask.FromResult(0);

    public ValueTask<bool> IsBountyClaimableAsync(string monsterId) => ValueTask.FromResult(true);

    public ValueTask<bool> GrantBountyAsync(string playerId, string monsterId, int amount)
        => ValueTask.FromResult(true);
}
