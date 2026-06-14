using System.Collections.Concurrent;
using Inventory.Shared;

namespace Inventory.Server;

/// <summary>
/// Root service. <see cref="OpenInventoryAsync"/> is the nested-services entry point —
/// it returns a real <see cref="PlayerInventory"/> instance, and the framework
/// transparently allocates an opaque token so the client's sub-proxy can call back
/// into the exact same object on subsequent RPCs.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly ConcurrentDictionary<string, Player> _players;
    private readonly IReadOnlyDictionary<string, int> _catalog;
    // One inventory per (connection, player) — keyed only by player here because the
    // ShaRPC instance registry is already per-connection.
    private readonly ConcurrentDictionary<string, PlayerInventory> _inventories = new();

    public InventoryService()
    {
        _players = new ConcurrentDictionary<string, Player>(StringComparer.Ordinal)
        {
            ["alice"] = new() { Id = "alice", Name = "Alice",   Gold = 250 },
            ["bob"]   = new() { Id = "bob",   Name = "Bob",     Gold = 80 },
            ["cleo"]  = new() { Id = "cleo",  Name = "Cleo",    Gold = 4200 },
        };
        _catalog = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["sword"]  = 100,
            ["shield"] = 75,
            ["potion"] = 10,
            ["bread"]  = 2,
        };
    }

    public Player GetPlayer(string playerId)
    {
        if (_players.TryGetValue(playerId, out var p)) return p;
        throw new KeyNotFoundException($"Unknown player '{playerId}'.");
    }

    public IReadOnlyList<string> ListPlayerIds() => _players.Keys.OrderBy(k => k).ToList();

    public Task<IPlayerInventory> OpenInventoryAsync(string playerId, CancellationToken ct = default)
    {
        if (!_players.ContainsKey(playerId))
        {
            throw new KeyNotFoundException($"Unknown player '{playerId}'.");
        }
        var inv = _inventories.GetOrAdd(playerId, id => new PlayerInventory(id, _catalog));
        Console.WriteLine($"  opened inventory for {playerId}");
        return Task.FromResult<IPlayerInventory>(inv);
    }
}
