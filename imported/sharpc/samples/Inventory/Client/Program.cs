using Inventory.Shared;
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

const string Host = "localhost";
const int Port = 5051;

Console.WriteLine("ShaRPC Inventory Client");
Console.WriteLine("=======================");
Console.WriteLine();

var transport = new TcpTransport(Host, Port);
var serializer = new MessagePackRpcSerializer();

try
{
    Console.WriteLine($"Connecting to {Host}:{Port}...");
    await transport.ConnectAsync();
    await using var peer = RpcPeer
        .Over(transport.Connection!, serializer, new RpcPeerOptions { RequestTimeout = TimeSpan.FromSeconds(10) })
        .Start();
    Console.WriteLine("Connected!");
    Console.WriteLine();

    // The generator emits two interfaces and one proxy class for IInventoryService:
    //   - IInventoryService       (the user's contract, sync methods stay sync)
    //   - IInventoryServiceAsync  (auto-generated sibling; everything returns Task[<T>])
    //   - InventoryServiceProxy   (implements BOTH so callers pick the view they want)
    var inventoryProxy = peer.GetInventoryService();

    // -------------- 1. Sync call via the original interface (blocks) --------------
    Console.WriteLine("[sync]  blocking call on IInventoryService.GetPlayer(\"alice\"):");
    Player alice = inventoryProxy.GetPlayer("alice");
    Console.WriteLine($"        -> {alice.Name} ({alice.Gold} gold)");
    Console.WriteLine();

    // -------------- 2. Same logical call, via the async sibling --------------
    //
    // The proxy ALSO implements IInventoryServiceAsync, so we can pull the same
    // logical operation through a non-blocking entry point that returns Task<T>
    // and accepts a CancellationToken. This is the recommended path in UI / server
    // code where blocking the thread is unacceptable.
    Console.WriteLine("[async] non-blocking call via IInventoryServiceAsync.GetPlayerAsync(\"bob\"):");
    // The proxy class implements both interfaces, so this is a safe downcast at
    // runtime — the API surface forces you to be explicit about which view you want.
    var asyncInventory = (IInventoryServiceAsync)inventoryProxy;
    Player bob = await asyncInventory.GetPlayerAsync("bob");
    Console.WriteLine($"        -> {bob.Name} ({bob.Gold} gold)");
    Console.WriteLine();

    Console.WriteLine("[async] non-blocking list of player ids:");
    var ids = await asyncInventory.ListPlayerIdsAsync();
    Console.WriteLine("        -> " + string.Join(", ", ids));
    Console.WriteLine();

    // -------------- 3. Nested service — sub-proxy returned by the root --------------
    //
    // OpenInventoryAsync returns Task<IPlayerInventory>, but IPlayerInventory is itself
    // a [ShaRpcService]. The generator wires this so the wire response is an opaque
    // ServiceHandle, and the value handed back to us is a working sub-proxy bound to
    // the exact server-side PlayerInventory instance the root created. Every call on
    // `cleoInventory` below routes to that same object.
    Console.WriteLine("[nested] opening Cleo's inventory (sub-service proxy)...");
    IPlayerInventory cleoInventory = await inventoryProxy.OpenInventoryAsync("cleo");
    Console.WriteLine($"        -> got {cleoInventory.GetType().Name}, bound to a server-side instance");
    Console.WriteLine();

    Console.WriteLine("[nested] adding items to Cleo's inventory:");
    await cleoInventory.AddItemAsync("sword", 1);
    await cleoInventory.AddItemAsync("potion", 5);
    await cleoInventory.AddItemAsync("bread", 12);

    var items = await cleoInventory.ListItemsAsync();
    Console.WriteLine("        current items:");
    foreach (var it in items)
    {
        Console.WriteLine($"          - {it.Name} x{it.Quantity} @ {it.UnitValue}g");
    }

    var total = await cleoInventory.GetTotalValueAsync();
    Console.WriteLine($"        total value: {total} gold");
    Console.WriteLine();

    // Re-opening the same player's inventory in the same connection returns the same
    // server-side instance — so the items we just added are still there.
    Console.WriteLine("[nested] re-opening Cleo's inventory in the same connection:");
    var cleoAgain = await inventoryProxy.OpenInventoryAsync("cleo");
    var itemsAgain = await cleoAgain.ListItemsAsync();
    Console.WriteLine($"        still {itemsAgain.Count} item type(s) — state survived the round-trip");
    Console.WriteLine();

    // Opening a different player gives a different server-side instance.
    Console.WriteLine("[nested] opening Alice's inventory (a different server-side instance):");
    var aliceInventory = await inventoryProxy.OpenInventoryAsync("alice");
    var aliceTotal = await aliceInventory.GetTotalValueAsync();
    Console.WriteLine($"        Alice's total value: {aliceTotal} gold (separate from Cleo's)");
    Console.WriteLine();

    Console.WriteLine("All inventory operations completed successfully.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex);
}
finally
{
    await transport.DisposeAsync();
    Console.WriteLine("\nClient disconnected.");
}
