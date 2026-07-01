---
title: 'Tutorial: your first Service (RPC)'
description: 'A Service is the first of DotBoxD''s three ways to use one C# contract: the host implements the contract, and clients call it remotely over RPC. You write…'
---
A **Service** is the first of DotBoxD's three ways to use one C# contract: the host implements the
contract, and clients call it remotely over RPC. You write one `[DotBoxDService]` interface, a Roslyn
source generator emits the typed proxy and dispatcher at compile time (no runtime reflection on the hot
path), and a MessagePack named-pipe transport carries the calls.

By the end of this page you will have a host process that serves a contract and a client process that
calls it over a named pipe in one round-trip per method. Every API used here is exercised by the
maintained sample under
[`samples/GameServer`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer); the pieces are shown in the README's
["1. Services"](https://github.com/JKamsker/DotBoxD/blob/main/README.md) section.

## Why Services (RPC)? (and when to use it)

Services exist to make interop easy. Hand-writing RPC marshaling — build a request envelope, serialize
args, match a response to its call, deserialize, cast — is repetitive and easy to get subtly wrong. With
DotBoxD you annotate one interface with `[DotBoxDService]` and a Roslyn source generator emits three
artifacts at compile time: a **typed client proxy** (what `Get<T>()` returns), a **server dispatcher**
that decodes a request and invokes your implementation, and the `Provide{Service}` / `Get<T>()`
extensions
([generator wiring](https://github.com/JKamsker/DotBoxD/blob/main/src/CodeGeneration/DotBoxD.Services.SourceGenerator/EntryPoint/DotBoxDRpcGenerator.cs)).
The payoff: your implementation is *just your logic* — nothing DotBoxD-specific leaks into it, and the
client calls `connection.Get<ICatalogService>().GetUnitPriceAsync("sword")` as one typed round-trip.

A few grounded reasons this design earns its place:

- **The interface is the single source of truth, so proxy and impl can't drift.** Both are generated
  from the same C# shape, so a rename or type change is a compile error, not a runtime wire fault.
  Unsupported shapes surface as build-time diagnostics (`DBXS001`–`DBXS004`) — for example a
  `ref`/`in`/`out` parameter or a generic/nested interface is rejected at compile time.
- **No runtime reflection on the hot path.** Proxy/dispatcher lookup goes through a *generated* registry
  rather than scanning assemblies, and the MessagePack codec uses generated formatters. That is why the
  Services stack targets `netstandard2.1` and runs on **Unity / IL2CPP** and NativeAOT, where runtime
  reflection and dynamic codegen are stripped or forbidden.
- **Peer-based and bidirectional.** A connection is a symmetric `RpcPeer`: the same object can `Provide`
  local services and `Get` proxies for remote ones over one read loop, so the host can call *back* into
  a connecting plugin over the same wire — no separate client/server class on the hot path.
- **Transport- and codec-neutral.** The same contract runs over named pipes, TCP, WebSocket, or an
  in-process test channel, with MessagePack (or another `ISerializer`) as the codec — the generated
  proxy, dispatcher, and `Provide`/`Get` extensions are identical either way.

**When to use a Service:** the host owns a capability and the client needs a typed request→response it
can `await`, the interaction is a bounded number of discrete calls (one method = one round-trip), you
need host↔plugin callbacks on one connection, or you need Unity/IL2CPP reach.

**When to prefer another mode:** reach for the [event pipeline (RunLocal)](/tutorials/event-pipeline-runlocal/)
to react to high-frequency events, or [Pushdown](/concepts/pushdown/) to collapse a chatty N-call
loop into one server-side batch. Services (RPC) is a *trusted* channel; the sandbox trust boundary lives
in Kernels/Pushdown, not here.

## What you'll build

Three pieces, mirroring how the GameServer sample is laid out (shared abstractions, server, client):

- A **shared contract** project holding the `[DotBoxDService]` interface and its DTOs (data transfer objects — the plain data types that cross the wire).
- A **host** that implements the contract and listens on a named pipe.
- A **client** that connects and calls the contract through a generated proxy.

## Prerequisites

- **.NET SDK 10** — required, because the host and client in this tutorial target `net10.0`. An SDK 8
  or 9 alone cannot build `net10.0` and fails the `dotnet add package DotBoxD` restore below with a
  cryptic `NU1202`/`NETSDK1045` error. (The .NET 8 and 9 *runtimes* only matter for the
  `netstandard2.1` Services stack or the full test suite — not for building this walkthrough.) The
  named-pipe IPC helper used below (`RpcMessagePackIpc`) ships in `DotBoxD.Pushdown.Services`, which
  targets `net10.0`, so target `net10.0` for the host and client in this tutorial. (The pure
  Services/channel stack is `netstandard2.1` and also runs on Unity/IL2CPP — see
  [Unity note](#unity-and-the-netstandard21-stack) at the end.)

## Step 1 — Create the projects and install the package

```bash
dotnet new classlib -n MyApp.Contracts
dotnet new console  -n MyApp.Host
dotnet new console  -n MyApp.Client

# The host and client reference the shared contract project:
dotnet add MyApp.Host    reference MyApp.Contracts
dotnet add MyApp.Client  reference MyApp.Contracts
```

> **Target `net10.0` in all three projects.** `MyApp.Contracts`, `MyApp.Host`, and `MyApp.Client` each
> reference DotBoxD, so each must target `net10.0`. Set `<TargetFramework>net10.0</TargetFramework>` in
> every `.csproj`, or pass `-f net10.0` to each `dotnet new` command above.

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

## Step 5 — Connect a client and get the typed proxy

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

You should now see the host terminal print:

```text
Catalog host listening on pipe: myapp-catalog-…
Press Enter to stop.
```

and the client terminal print (sword = 100, shield = 75):

```text
sword unit price = 100
cart: 2 items, total = 175
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

- [**Event pipelines (RunLocal)**](/tutorials/event-pipeline-runlocal/) — the next tutorial: subscribe to a
  server event, push the `Where`/`Select` filter server-side, and react locally.
- [Services concepts](/concepts/services/) — the dispatch model, peers/hosts, and streaming.
- [Kernels concept](/concepts/kernels/) — run *validated client logic* inside a metered
  sandbox (the second of the three modes).
- [Project README](https://github.com/JKamsker/DotBoxD/blob/main/README.md) — the three modes side by side, the full package matrix, and the
  security/trust boundary.
