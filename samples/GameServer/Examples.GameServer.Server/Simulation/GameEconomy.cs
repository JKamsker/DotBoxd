using DotBoxD.Kernels.Game.Server.Abstractions.Events;
using DotBoxD.Plugins.Indexing;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Game.Server.Simulation;

internal sealed class GameEconomy
{
    private const int StartingTreasury = 500;
    private const int PayoutBudgetPerTick = 120;

    private readonly Dictionary<string, int> _balances = new(StringComparer.Ordinal)
    {
        ["treasury"] = StartingTreasury,
        ["player-1"] = 20,
        ["player-2"] = 10
    };
    private readonly HashSet<string> _claimableBounties = new(StringComparer.Ordinal);
    private readonly HashSet<string> _claimedBounties = new(StringComparer.Ordinal);
    private readonly SubscriptionRegistry _subscriptions;
    private readonly EventIndexRegistry _indexRegistry;
    private int _remainingTickBudget = PayoutBudgetPerTick;

    public GameEconomy(SubscriptionRegistry subscriptions, EventIndexRegistry indexRegistry)
    {
        _subscriptions = subscriptions;
        _indexRegistry = indexRegistry;
    }

    public void ResetTickBudget() => _remainingTickBudget = PayoutBudgetPerTick;

    public int GetBalance(string entityId) => _balances.GetValueOrDefault(entityId);

    public void MarkBountyClaimable(string monsterId) => _claimableBounties.Add(monsterId);

    public bool IsBountyClaimable(string monsterId)
        => _claimableBounties.Contains(monsterId) && !_claimedBounties.Contains(monsterId);

    public bool GrantBounty(string playerId, string monsterId, int amount)
    {
        if (amount <= 0 ||
            !IsBountyClaimable(monsterId) ||
            amount > _remainingTickBudget ||
            GetBalance("treasury") < amount)
        {
            return false;
        }

        _claimedBounties.Add(monsterId);
        _remainingTickBudget -= amount;
        AddBalance("treasury", -amount, "bounty:" + monsterId);
        AddBalance(playerId, amount, "bounty:" + monsterId);
        return true;
    }

    public IReadOnlyDictionary<string, int> Balances()
        => new SortedDictionary<string, int>(_balances, StringComparer.Ordinal);

    public IReadOnlyCollection<string> Claims()
        => _claimedBounties.Order(StringComparer.Ordinal).ToArray();

    private void AddBalance(string entityId, int delta, string reason)
    {
        var balance = GetBalance(entityId) + delta;
        _balances[entityId] = balance;
        var changed = new GoldChangedEvent(entityId, balance, delta, reason);
        _subscriptions.Publish(changed);
        _indexRegistry.Publish(changed);
    }
}
