# Inventory sample

Demonstrates two source-generator features in one round-tripping client/server pair:

| Feature | What you see in this sample |
| --- | --- |
| **Async sibling interface** | `IInventoryService` declares `GetPlayer` / `ListPlayerIds` as **sync** methods. The generator emits a sibling `IInventoryServiceAsync` exposing `GetPlayerAsync` / `ListPlayerIdsAsync`. The generated proxy implements **both** interfaces — the client casts to the sibling whenever it needs a non-blocking call. |
| **Nested services** | `IInventoryService.OpenInventoryAsync` returns `Task<IPlayerInventory>`, where `IPlayerInventory` is itself a `[ShaRpcService]`. The generator wires this so the wire response is a `ServiceHandle`, and the value returned to the client is a `PlayerInventoryProxy` bound to the exact server-side instance the root method created. Every subsequent call on the sub-proxy lands back on that same object. |

## Run

In one terminal:

```sh
cd samples/Inventory/Server
dotnet run
```

In another:

```sh
cd samples/Inventory/Client
dotnet run
```

Expected client output (excerpt):

```
[sync]  blocking call on IInventoryService.GetPlayer("alice"):
        -> Alice (250 gold)

[async] non-blocking call via IInventoryServiceAsync.GetPlayerAsync("bob"):
        -> Bob (80 gold)

[nested] opening Cleo's inventory (sub-service proxy)...
        -> got PlayerInventoryProxy, bound to a server-side instance

[nested] adding items to Cleo's inventory:
        current items:
          - sword x1 @ 100g
          - potion x5 @ 10g
          - bread x12 @ 2g
        total value: 174 gold

[nested] re-opening Cleo's inventory in the same connection:
        still 3 item type(s) — state survived the round-trip
```

The server output confirms the per-connection instance registry holds Cleo's `PlayerInventory`:

```
opened inventory for cleo
  [cleo] +1 sword (total 1)
  [cleo] +5 potion (total 5)
  [cleo] +12 bread (total 12)
opened inventory for cleo                 <-- second open returns the SAME instance
opened inventory for alice                <-- different player, different instance
```

## Wire mechanics

- A sub-service handle round-trips as a tiny `ServiceHandle { ServiceName, InstanceId }`.
- Each connection gets one `IInstanceRegistry`; instances are released when the connection closes.
- The proxy class has two constructors: `(IRpcInvoker)` for a top-level proxy and
  `(IRpcInvoker, string instanceId)` for an instance-scoped one — the latter is what
  the parent proxy instantiates when wrapping a returned `ServiceHandle`.
- The dispatcher class implements both `DispatchAsync` (singleton routing) and
  `DispatchOnInstanceAsync` (instance routing); the server picks based on whether the
  inbound `RpcRequest.InstanceId` is set.

For the full design see [`docs/design/nested-services.md`](../../docs/design/nested-services.md).
