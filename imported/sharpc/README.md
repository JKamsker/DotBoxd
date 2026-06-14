# ShaRPC

[![ci](https://github.com/JKamsker/sharpc/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/JKamsker/sharpc/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/ShaRPC.svg?logo=nuget&label=ShaRPC)](https://www.nuget.org/packages/ShaRPC)
[![Downloads](https://img.shields.io/nuget/dt/ShaRPC.svg?logo=nuget)](https://www.nuget.org/packages/ShaRPC)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A high-performance, transport-agnostic RPC framework for C# with source generator-based code generation, designed for Unity and .NET interoperability.

## Installation

Install from [nuget.org](https://www.nuget.org/packages/ShaRPC). The main package
bundles the runtime core **and** the source generator, so this is all you need to
define and call services:

```sh
# Core runtime + source generator (start here)
dotnet add package ShaRPC
```

Add a serializer:

```sh
# MessagePack serializer (ISerializer implementation)
dotnet add package ShaRPC.Serializers.MessagePack
```

Add a transport:

```sh
# TCP server/client transport
dotnet add package ShaRPC.Transports.Tcp

# Named-pipe server/client transport for process-boundary IPC
dotnet add package ShaRPC.Transports.NamedPipes
```

A typical TCP + MessagePack setup uses all three:

```sh
# Everything needed for the Quick Start below
dotnet add package ShaRPC
dotnet add package ShaRPC.Serializers.MessagePack
dotnet add package ShaRPC.Transports.Tcp
```

## Features

- **Source Generator Based**: Compile-time proxy and dispatcher generation — no runtime reflection.
- **Generated service catalog**: `ShaRpcGenerated.Services` exposes service/proxy/dispatcher descriptors without scanning assemblies.
- **Generic registration sink**: `ShaRpcGenerated.RegisterServices(...)` calls host-provided generic registration APIs per generated service.
- **Generated implementation sink**: `ShaRpcGenerated.RegisterGeneratedServices(...)` emits direct service/proxy/dispatcher generic registrations.
- **Truly incremental generator**: value-equatable models, `ForAttributeWithMetadataName`, tracked steps. The IDE never re-runs unnecessary work, even across large edits.
- **Async sibling interfaces**: every `[ShaRpcService]` automatically gains an `I{Name}Async` view so callers can pick a blocking or non-blocking entry point.
- **Nested services**: a method returning another `[ShaRpcService]` interface returns a fully-working sub-proxy bound to a server-side instance — no DTO marshalling for live objects.
- **Unity Compatible**: Works with IL2CPP and AOT compilation.
- **Bidirectional peers**: one duplex connection can serve local services and call remote services at the same time.
- **Transport Agnostic**: TCP, dedicated named-pipe transports, stream connections, and single-connection adapters included; easily extensible to WebSocket, Steam, etc.
- **Shared Contracts**: Same C# interfaces on client and server.
- **Fast Serialization**: MessagePack for efficient binary encoding, custom resolver options, and `ReadOnlyMemory<byte>` DTO fields.
- **Async/Await**: Full async support with cancellation tokens.

## Quick Start

### 1. Define Service Contract

```csharp
[ShaRpcService]
public interface IGameService
{
    Task<PlayerState> JoinAsync(string playerName, CancellationToken ct = default);
    Task<ActionResult> MoveAsync(MoveRequest request, CancellationToken ct = default);
}
```

### 2. Host the Service

A `RpcHost` accepts connections and turns each one into an `RpcPeer`. The
generator emits a `Provide{Service}` extension per `[ShaRpcService]`:

```csharp
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

await using var host = RpcHost
    .Listen(new TcpServerTransport(7777), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvideGameService(new GameService()));

await host.StartAsync();
// host.PeerConnected / host.PeerDisconnected / host.AcceptError surface lifecycle events.
// await host.StopAsync();  closes every accepted peer (DisposeAsync also stops).
```

### 3. Call the Service

The caller connects a transport as a peer session and asks the generator for a
proxy via `Get{Service}`. `RejectInboundCalls = true`
declares a call-only peer that refuses any callbacks from the other side:

```csharp
var transport = new TcpTransport("localhost", 7777);
await using var session = await transport.ConnectPeerAsync(
    new MessagePackRpcSerializer(),
    new RpcPeerOptions { RejectInboundCalls = true });

var gameService = session.Peer.GetGameService();
var player = await gameService.JoinAsync("Player1");
```

`RpcPeerSession` owns both the connected transport and the peer, so disposing the
session closes both. If you already have an `IRpcChannel`, use
`RpcPeer.Over(channel, serializer)` directly.

### Bidirectional Peer

For named pipes, plugin IPC, sidecars, and other duplex transports, both endpoints can serve and call services over one connection. Each side is an `RpcPeer` over the same duplex `IRpcChannel`, and each may both `Provide` services and `Get` proxies:

```csharp
// One side: provide IGameService, call back into IRemoteGameService.
await using var peer = RpcPeer
    .Over(connection, new MessagePackRpcSerializer())
    .ProvideGameService(gameService)
    .Start();

var remote = peer.GetRemoteGameService();
```

`ProvideGameService` / `GetRemoteGameService` are generator-emitted shortcuts.
If you hold the dispatcher or proxy yourself, the generic factory and the
`Provide` / `IRpcInvoker` overloads do the same thing:

```csharp
await using var peer = RpcPeer
    .Over(connection, new MessagePackRpcSerializer())
    .Provide(ShaRpcGenerated.CreateDispatcher<IGameService>(gameService))
    .Start();

var remote = ShaRpcGenerated.CreateProxy<IRemoteGameService>(peer);
```

Named-pipe IPC uses a dedicated transport package:

```csharp
using ShaRPC.Transports.NamedPipes;

var serverTransport = new NamedPipeServerTransport("my-plugin-pipe");
var clientTransport = new NamedPipeClientTransport("my-plugin-pipe");
```

## Generator features at a glance

### Async sibling interface

Declare a synchronous method on a `[ShaRpcService]` interface and the generator
emits a sibling interface so callers can avoid blocking:

```csharp
[ShaRpcService]
public interface IInventoryService
{
    Player GetPlayer(string playerId);            // sync
    Task<IPlayerInventory> OpenInventoryAsync(string playerId, CancellationToken ct = default);
}

// generated alongside, in the same namespace:
public interface IInventoryServiceAsync
{
    Task<Player> GetPlayerAsync(string playerId, CancellationToken ct = default);
    Task<IPlayerInventory> OpenInventoryAsync(string playerId, CancellationToken ct = default);
}
```

The generated proxy class implements **both** interfaces. Existing async methods
already match between the two views, so only sync members produce an extra
overload.

### Nested services

A method whose return type is itself a `[ShaRpcService]` interface returns a
generated sub-proxy bound to the server-side instance the root call produced.
Subsequent calls on the sub-proxy go back to that exact object.

```csharp
[ShaRpcService] public interface IInventoryService
{
    Task<IPlayerInventory> OpenInventoryAsync(string playerId, CancellationToken ct = default);
}

[ShaRpcService] public interface IPlayerInventory
{
    Task<int> AddItemAsync(string itemId, int quantity, CancellationToken ct = default);
    Task<IReadOnlyList<Item>> ListItemsAsync(CancellationToken ct = default);
}

// caller side:
var inventoryProxy = peer.GetInventoryService();
var inv = await inventoryProxy.OpenInventoryAsync("cleo");   // returns a real sub-proxy
await inv.AddItemAsync("sword", 1);                          // routes back to the same server instance
```

See [`docs/design/nested-services.md`](docs/design/nested-services.md) for the
wire protocol, lifetime model, and explicit non-goals.

## Project Structure

```
sharpc/
├── src/
│   ├── ShaRPC.Core/                    # Core abstractions and infrastructure
│   ├── ShaRPC.SourceGenerator/         # Compile-time code generation
│   ├── ShaRPC.Transports.Tcp/          # TCP transport implementation
│   ├── ShaRPC.Transports.NamedPipes/   # Named-pipe transport implementation
│   └── ShaRPC.Serializers.MessagePack/ # MessagePack serialization
├── samples/
│   ├── GameService/{Shared,Server,Client}/    # Original sample (TCP + MessagePack)
│   └── Inventory/{Shared,Server,Client}/      # Async-sibling + nested-services demo
├── tests/
│   ├── ShaRPC.Tests/                   # Core integration tests
│   └── ShaRPC.SourceGenerator.Tests/   # Generator unit + snapshot + behavioural tests
└── docs/
    ├── quick-start.md                  # Getting started guide
    ├── unity-integration.md            # Unity integration guide
    ├── named-pipe-transport.md         # Named-pipe IPC setup
    ├── api-reference.md                # API documentation
    └── design/nested-services.md       # Nested-services design
```

## Samples

### `samples/GameService/`

The classic player-state RPC scenario — register, move, perform actions. Good
introduction to the framework's basic shape.

### `samples/Inventory/`

End-to-end demo of the **async sibling** and **nested services** features.
Run the server in one terminal, the client in another:

```bash
dotnet run --project samples/Inventory/Server     # listens on :5051
dotnet run --project samples/Inventory/Client     # connects, walks all three call paths
```

The client output shows a blocking sync call, a non-blocking call via the
generated async sibling, and a sub-service obtained from the root that
preserves server-side state across calls within a connection. See
[`samples/Inventory/README.md`](samples/Inventory/README.md) for details.

## Documentation

- [Quick Start Guide](docs/quick-start.md)
- [Unity Integration Guide](docs/unity-integration.md)
- [Generated Service Registry](docs/generated-service-registry.md)
- [Performance Hot Paths](docs/performance.md)
- [Named Pipe Transport](docs/named-pipe-transport.md)
- [API Reference](docs/api-reference.md)
- [Nested Services Design](docs/design/nested-services.md)

## Building

```bash
# Build all projects
dotnet build

# Run generator tests (fast)
dotnet test tests/ShaRPC.SourceGenerator.Tests

# Run core integration tests
dotnet test tests/ShaRPC.Tests

# Run sample server
dotnet run --project samples/GameService/Server

# Run sample client (in another terminal)
dotnet run --project samples/GameService/Client
```

## NuGet packages

CI publishes to [nuget.org](https://www.nuget.org/packages/ShaRPC) on every push to
`main` (as `1.0.0-ci.<run_number>` prereleases); tag pushes (`vX.Y.Z`) are released
under the tag's exact version.

| Package                                                                                           | Contents                                                                  |
|---------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------|
| [`ShaRPC`](https://www.nuget.org/packages/ShaRPC)                                                 | Runtime core + bundled source generator (auto-loaded analyzer, no runtime dependency) |
| [`ShaRPC.Transports.Tcp`](https://www.nuget.org/packages/ShaRPC.Transports.Tcp)                   | TCP server/client transport                                               |
| [`ShaRPC.Transports.NamedPipes`](https://www.nuget.org/packages/ShaRPC.Transports.NamedPipes)     | Named-pipe server/client transport for local IPC                          |
| [`ShaRPC.Serializers.MessagePack`](https://www.nuget.org/packages/ShaRPC.Serializers.MessagePack) | MessagePack `ISerializer`                                                 |

The core library and the source generator ship together in the single `ShaRPC`
package. Transport and serializer packages depend
on `ShaRPC`. See [Installation](#installation) for the install commands.

## Requirements

- **.NET Standard 2.1** for library projects (Unity 2021.3+).
- **.NET 6.0+** for server projects (recommended: .NET 8.0+; CI builds and tests against .NET 8 and 9).
- **MessagePack** 2.5.x.

## Diagnostics emitted by the generator

| Id        | Severity | Meaning |
|-----------|----------|---------|
| SHARPC001 | Error    | Generator itself crashed while processing a service. |
| SHARPC002 | Error    | Method has an unsupported shape (e.g. `ref`/`in`/`out` parameter). A throwing stub is emitted so the proxy still implements the interface. |
| SHARPC003 | Error    | Service interface has an unsupported shape (generic or nested). Nothing is emitted for that service. |
| SHARPC004 | Warning  | Async-sibling projection would collide with another method (e.g. a sync `Foo` next to an existing async `FooAsync`). The colliding row is dropped from the sibling. |

## Why ShaRPC?

| Feature               | ShaRPC       | gRPC        | SignalR     | [Dan.Net](https://github.com/danqzq/dan.net)      |
|-----------------------|--------------|-------------|-------------|---------------------------------------------------|
| Unity IL2CPP          | Yes          | Complex     | Limited     | Yes                                               |
| Shared C# contracts   | Yes          | Proto files | Hub methods | Interfaces (`ISyncData` and inheritance patterns) |
| Transport agnostic    | Yes          | HTTP/2 only | WebSocket   | WebSocket only                                    |
| Binary serialization  | MessagePack  | Protobuf    | JSON        | JSON/Binary streams                               |
| Code generation       | Compile-time | Build step  | Runtime     | None                                              |
| Async sibling         | Yes          | No          | No          | No                                                |
| Nested service refs   | Yes          | No          | No          | No                                                |

## License

[MIT](LICENSE)
