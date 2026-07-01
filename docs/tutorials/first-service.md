# Tutorial: your first Service (RPC)

A **Service** is the first of DotBoxD's three ways to use one C# contract: the host implements the
contract, and clients call it remotely over RPC. You write one `[DotBoxDService]` interface, a Roslyn
source generator emits the typed proxy and dispatcher at compile time (no runtime reflection on the hot
path), and a MessagePack named-pipe transport carries the calls.

By the end of this page you will have a host process that serves a contract and a client process that
calls it over a named pipe in one round-trip per method. Every API used here is exercised by the
maintained sample under
[`samples/GameServer`](https://github.com/JKamsker/DotBoxD/blob/main/README.md); the pieces are shown in the README's
["1. Services"](https://github.com/JKamsker/DotBoxD/blob/main/README.md) section.

## What you'll build

Three pieces, mirroring how the GameServer sample is laid out (shared abstractions, server, client):

- A **shared contract** project holding the `[DotBoxDService]` interface and its DTOs.
- A **host** that implements the contract and listens on a named pipe.
- A **client** that connects and calls the contract through a generated proxy.

## Prerequisites

- .NET SDK 8, 9, or 10. The named-pipe IPC helper used below (`RpcMessagePackIpc`) ships in
  `DotBoxD.Pushdown.Services`, which targets `net10.0`, so target `net10.0` for the host and client in
  this tutorial. (The pure Services/channel stack is `netstandard2.1` and also runs on Unity/IL2CPP —
  see [Unity note](#unity-and-the-netstandard21-stack) at the end.)

## Step 1 — Create the projects and install the package

```bash
dotnet new classlib -n MyApp.Contracts
dotnet new console  -n MyApp.Host
dotnet new console  -n MyApp.Client

# The host and client reference the shared contract project:
dotnet add MyApp.Host    reference MyApp.Contracts
dotnet add MyApp.Client  reference MyApp.Contracts
```

Add DotBoxD to each project. The meta-package `DotBoxD` pulls the full net10.0 stack (Services +
Kernels + Pushdown), which includes the `RpcMessagePackIpc` named-pipe helper and the bundled
`DotBoxD.Services.SourceGenerator`:

```bash
dotnet add MyApp.Contracts package DotBoxD --prerelease
dotnet add MyApp.Host      package DotBoxD --prerelease
dotnet add MyApp.Client    package DotBoxD --prerelease
```

> `--prerelease` is required while the net10.0 stack is in preview; drop it once you target a stable
> tag release. See the README "Quick start" and "Packages" tables (`README.md`) for the exact package
> matrix. `DotBoxD.Services.SourceGenerator` is bundled inside the Services package as an analyzer
> asset — you never add it as a standalone package.

## Step 2 — Define the `[DotBoxDService]` contract

Put the interface and its DTOs in the shared project so the host and the client compile against the
exact same shape. This is the contract from the README's Services example
(`README.md`, section "1. Services"):

```csharp
// MyApp.Contracts/ICatalogService.cs
using DotBoxD.Services.Attributes;

// One contract, shared by host and client.
[DotBoxDService]
public interface ICatalogService
{
    ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default);
    ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken cancellationToken = default);
}
```

The wire codec is MessagePack, so any DTO that crosses the wire is annotated the same way the
GameServer sample annotates its IPC payloads in
`samples/GameServer/Examples.GameServer.Server.Abstractions/Ipc/GameIpcContracts.cs`
(`[MessagePackObject]` + a stable `[Key]` per member, and a `[SerializationConstructor]`):

```csharp
// MyApp.Contracts/CatalogModels.cs
using MessagePack;

[MessagePackObject]
public readonly struct Cart
{
    [SerializationConstructor]
    public Cart(string[] itemIds) => ItemIds = itemIds;

    [Key(0)]
    public string[] ItemIds { get; }
}

[MessagePackObject]
public readonly struct CartTotal
{
    [SerializationConstructor]
    public CartTotal(int itemCount, int total)
    {
        ItemCount = itemCount;
        Total = total;
    }

    [Key(0)]
    public int ItemCount { get; }

    [Key(1)]
    public int Total { get; }
}
```

Two attributes are worth knowing, both in `DotBoxD.Services.Attributes`:

- `[DotBoxDService]` marks the interface; it has an optional `Name` property to override the wire
  service name (default: the interface name). See
  `src/Services/DotBoxD.Services/Attributes/DotBoxDServiceAttribute.cs`.
- `[DotBoxDMethod]` is **optional** on methods — every method in the interface is included by default.
  Use it only to customize a method (e.g. its `Name`). See
  `src/Services/DotBoxD.Services/Attributes/DotBoxDMethodAttribute.cs`.

> Contract shape rules: keep methods to plain parameters plus an optional trailing
> `CancellationToken`, and return `Task`/`Task<T>`/`ValueTask`/`ValueTask<T>`. `ref`/`in`/`out`
> parameters and generic or nested service interfaces are rejected at compile time — see the
> [diagnostics](#step-6--what-the-source-generator-emits) below.

## Step 3 — Implement the contract on the host

The host writes an ordinary class that implements the interface. Nothing DotBoxD-specific leaks into
the implementation — it is just your logic:

```csharp
// MyApp.Host/CatalogService.cs
public sealed class CatalogService : ICatalogService
{
    private readonly IReadOnlyDictionary<string, int> _prices;

    public CatalogService(IReadOnlyDictionary<string, int> prices) => _prices = prices;

    public ValueTask<int> GetUnitPriceAsync(string itemId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_prices.TryGetValue(itemId, out var price) ? price : 0);

    public ValueTask<CartTotal> ComputeCartTotalAsync(Cart cart, CancellationToken cancellationToken = default)
    {
        var total = 0;
        foreach (var id in cart.ItemIds)
            total += _prices.TryGetValue(id, out var price) ? price : 0;
        return ValueTask.FromResult(new CartTotal(cart.ItemIds.Length, total));
    }
}
```

## Step 4 — Host it with `RpcMessagePackIpc.ListenNamedPipe`

`RpcMessagePackIpc.ListenNamedPipe` (from `DotBoxD.Pushdown.Services`) turns every accepted connection
into an `RpcPeer`, and your callback registers the service on that peer via the generated
`Provide{Service}` extension. Then `StartAsync()` begins accepting connections:

```csharp
// MyApp.Host/Program.cs
using DotBoxD.Pushdown.Services;   // RpcMessagePackIpc
using DotBoxD.Services.Generated;  // generated ProvideCatalogService(...)

var prices = new Dictionary<string, int> { ["sword"] = 100, ["shield"] = 75 };

// A high-entropy pipe name — see the entropy note below.
var pipeName = "myapp-catalog-" + Guid.NewGuid().ToString("N");

// Turn every accepted connection into a peer that serves the contract.
await using var host = RpcMessagePackIpc.ListenNamedPipe(
    pipeName,
    peer => peer.ProvideCatalogService(new CatalogService(prices)));

await host.StartAsync();

Console.WriteLine($"Catalog host listening on pipe: {pipeName}");
Console.WriteLine("Press Enter to stop.");
Console.ReadLine();
```

`ListenNamedPipe` returns an `RpcHost` (`await host.StartAsync()` — see
`src/Services/DotBoxD.Services/Server/RpcHost.Lifecycle.cs`), and `await using` disposes it on exit. The
real GameServer wiring does the same thing — listen, then register the generated service on each peer —
in `samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginHost.cs`, which calls
`DotBoxDGeneratedExtensions.ProvideGamePluginControlService(peer, service)`.

> **Pipe-name entropy (real gotcha).** `RpcMessagePackIpc` validates the pipe name: by default it must
> be at least 32 characters with at least 8 distinct characters, so a guessable local pipe cannot be
> squatted. A `"prefix-" + Guid.NewGuid().ToString("N")` name satisfies this (that is exactly what the
> sample uses). For throwaway local development you may opt out with
> `NamedPipeTransportOptions.UnsafeDevelopment`. The validation lives in
> `src/Pushdown/DotBoxD.Pushdown.Services/RpcMessagePackIpc.cs`.

## Step 5 — Connect a client and call `connection.Get<TContract>()`

The client connects with `ConnectNamedPipeAsync` and asks the session for a strongly typed proxy via
the generated `Get<TContract>()`. Every call on that proxy is one remote round-trip:

```csharp
// MyApp.Client/Program.cs
using DotBoxD.Pushdown.Services;   // RpcMessagePackIpc
using DotBoxD.Services.Generated;  // generated Get<ICatalogService>()

var pipeName = args[0]; // the name the host printed

await using var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName);
var catalog = connection.Get<ICatalogService>();

var unitPrice = await catalog.GetUnitPriceAsync("sword");    // one remote round-trip
Console.WriteLine($"sword unit price = {unitPrice}");

var total = await catalog.ComputeCartTotalAsync(new Cart(["sword", "shield"]));
Console.WriteLine($"cart: {total.ItemCount} items, total = {total.Total}");
```

`ConnectNamedPipeAsync` returns an `RpcPeerSession` (see
`src/Pushdown/DotBoxD.Pushdown.Services/RpcMessagePackIpc.cs`), and `Get<ICatalogService>()` hands back
the generated proxy. The same client shape is verified end-to-end in
`samples/GameServer/Examples.GameServer.Plugin.Tests/Regression/GamePluginControlServiceIpcRegressionTests.cs`,
which does `connection.Get<IGamePluginControlService>()` and awaits a method that round-trips over the
pipe.

Run the two processes:

```bash
dotnet run --project MyApp.Host
# copy the printed pipe name, then in a second terminal:
dotnet run --project MyApp.Client -- myapp-catalog-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
```

## Step 6 — What the source generator emits

The `[DotBoxDService]` attribute drives `DotBoxD.Services.SourceGenerator` (bundled inside the Services
package). At compile time, for each annotated interface it emits:

- a **typed client proxy** — the object returned by `Get<ICatalogService>()`, which marshals each call
  onto the wire;
- a **server dispatcher** — decodes an inbound request, invokes your implementation, and encodes the
  response;
- the **`Provide{Service}` and `Get{Service}` / `Get<T>()` extensions** in the
  `DotBoxD.Services.Generated` namespace. `peer.ProvideCatalogService(impl)` registers your
  implementation on a host peer; `connection.Get<ICatalogService>()` resolves the proxy on a client
  session. Both a generic `Get<T>()` and a per-service form (e.g. `GetPluginEventCallback(peer)`) are
  generated — `samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginHost.cs` uses the per-service
  `GetPluginEventCallback(peer)` alongside the `Provide{Service}` forms, while the generic
  `connection.Get<T>()` is what a client uses to resolve a proxy (as in the IPC regression test).

Because everything is generated and there is no reflection on the hot path, contract mistakes surface as
**compile-time diagnostics** in the `DBXS####` namespace (services; kernels use `DBXK####`). The shipped
set — from `src/CodeGeneration/DotBoxD.Services.SourceGenerator/AnalyzerReleases.Shipped.md` — is:

| ID | Severity | Meaning |
|--------|----------|---------|
| `DBXS001` | Error | DotBoxD source generator failure |
| `DBXS002` | Error | Unsupported method shape (e.g. a `ref`/`in`/`out` parameter) |
| `DBXS003` | Error | Unsupported service shape (e.g. a generic or nested interface) |
| `DBXS004` | Warning | Async sibling interface method name collides with another method |

If you hit `DBXS002` or `DBXS003`, adjust the contract (drop the `ref`/`in`/`out` parameter, or lift the
interface out to a non-nested, non-generic top-level type) and rebuild.

## Step 7 — The maintained runnable example

This tutorial's shapes are lifted from the real, maintained example. When you want a fuller,
always-green reference — multiple services per connection, reverse callbacks, live settings, and server
extensions — run the GameServer sample:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

Real contracts to read next:
`samples/GameServer/Examples.GameServer.Server.Abstractions/Ipc/IGamePluginControlService.cs` and
`samples/GameServer/Examples.GameServer.Server.Abstractions/IGameWorldAccess.cs` (both
`[DotBoxDService]`), wired on the host in
`samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginHost.cs`.

### Unity and the netstandard2.1 stack

The pure Services/channel stack targets `netstandard2.1` and runs on **Unity / IL2CPP**. For a Unity
service bundle, install the meta-package instead:

```bash
dotnet add package DotBoxD.Services.All --prerelease
```

`RpcMessagePackIpc` itself is a net10.0 convenience wrapper (in `DotBoxD.Pushdown.Services`); on the
netstandard2.1 stack you compose the same `RpcHost` / `RpcPeer` primitives directly with the
`DotBoxD.Transports.NamedPipes` (or `.Tcp`) transport and the `DotBoxD.Codecs.MessagePack` codec. The
generated `[DotBoxDService]` proxy, dispatcher, and `Provide`/`Get` extensions are identical either way.

## Next steps

- [Services concepts](../concepts/services.md) — the dispatch model, peers/hosts, and streaming.
- [Kernels concept](../concepts/kernels.md) — run *validated client logic* inside a metered
  sandbox (the second of the three modes).
- [Project README](https://github.com/JKamsker/DotBoxD/blob/main/README.md) — the three modes side by side, the full package matrix, and the
  security/trust boundary.
