using ShaRPC.Core.Attributes;

namespace Inventory.Shared;

/// <summary>
/// Root inventory service. Notice two things:
///
///   1. <see cref="GetPlayer"/> and <see cref="ListPlayerIds"/> are declared
///      synchronously. The generator emits a sibling <c>IInventoryServiceAsync</c>
///      that exposes <c>GetPlayerAsync</c> / <c>ListPlayerIdsAsync</c> for callers
///      that must not block.
///
///   2. <see cref="OpenInventoryAsync"/> returns ANOTHER <c>[ShaRpcService]</c>.
///      The generator detects this and emits a proxy method that returns a
///      sub-service proxy bound to the server-side inventory instance that
///      <c>OpenInventoryAsync</c> created. Every subsequent call on the returned
///      <see cref="IPlayerInventory"/> hits the same server-side object — so add /
///      remove / query stay consistent across calls within a connection.
/// </summary>
[ShaRpcService]
public interface IInventoryService
{
    /// <summary>Synchronous — exists to show the async sibling generation.</summary>
    Player GetPlayer(string playerId);

    /// <summary>Synchronous — exists to show the async sibling generation.</summary>
    IReadOnlyList<string> ListPlayerIds();

    /// <summary>
    /// Opens a per-player inventory session. The returned <see cref="IPlayerInventory"/>
    /// is a sub-service proxy backed by a real server-side instance.
    /// </summary>
    Task<IPlayerInventory> OpenInventoryAsync(string playerId, CancellationToken ct = default);
}

/// <summary>
/// Sub-service. The client never instantiates this directly — it gets a working
/// proxy from <see cref="IInventoryService.OpenInventoryAsync"/>. The server-side
/// instance lives for the duration of the connection that opened it.
/// </summary>
[ShaRpcService]
public interface IPlayerInventory
{
    Task<IReadOnlyList<Item>> ListItemsAsync(CancellationToken ct = default);
    Task<int> AddItemAsync(string itemId, int quantity, CancellationToken ct = default);
    Task<int> RemoveItemAsync(string itemId, int quantity, CancellationToken ct = default);
    Task<int> GetTotalValueAsync(CancellationToken ct = default);
}
