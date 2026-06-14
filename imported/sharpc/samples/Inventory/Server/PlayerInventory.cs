using System.Collections.Concurrent;
using Inventory.Shared;

namespace Inventory.Server;

/// <summary>
/// One inventory per player. The InventoryService hands out an instance of this class
/// from <c>OpenInventoryAsync</c>; the framework registers it under an opaque token
/// for the duration of the connection, and every subsequent call on the client-side
/// sub-proxy lands back on this exact object.
/// </summary>
public sealed class PlayerInventory : IPlayerInventory
{
    private readonly ConcurrentDictionary<string, Item> _items = new();
    private readonly IReadOnlyDictionary<string, int> _catalog;
    private readonly string _playerId;

    public PlayerInventory(string playerId, IReadOnlyDictionary<string, int> catalog)
    {
        _playerId = playerId;
        _catalog = catalog;
    }

    public Task<IReadOnlyList<Item>> ListItemsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Item> snapshot = _items.Values
            .Select(i => new Item
            {
                Id = i.Id,
                Name = i.Name,
                UnitValue = i.UnitValue,
                Quantity = i.Quantity,
            })
            .ToList();
        return Task.FromResult(snapshot);
    }

    public Task<int> AddItemAsync(string itemId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (!_catalog.TryGetValue(itemId, out var unitValue))
        {
            throw new KeyNotFoundException($"Item '{itemId}' is not in the catalog.");
        }

        var updated = _items.AddOrUpdate(
            itemId,
            _ => new Item { Id = itemId, Name = itemId, UnitValue = unitValue, Quantity = quantity },
            (_, existing) =>
            {
                existing.Quantity += quantity;
                return existing;
            });

        Console.WriteLine($"  [{_playerId}] +{quantity} {itemId} (total {updated.Quantity})");
        return Task.FromResult(updated.Quantity);
    }

    public Task<int> RemoveItemAsync(string itemId, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (!_items.TryGetValue(itemId, out var existing))
        {
            return Task.FromResult(0);
        }

        var newQty = Math.Max(0, existing.Quantity - quantity);
        if (newQty == 0)
        {
            _items.TryRemove(itemId, out _);
        }
        else
        {
            existing.Quantity = newQty;
        }
        Console.WriteLine($"  [{_playerId}] -{quantity} {itemId} (now {newQty})");
        return Task.FromResult(newQty);
    }

    public Task<int> GetTotalValueAsync(CancellationToken ct = default)
    {
        var total = _items.Values.Sum(i => i.UnitValue * i.Quantity);
        return Task.FromResult(total);
    }
}
