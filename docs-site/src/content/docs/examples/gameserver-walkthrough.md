---
title: 'Example: the GameServer sample, end to end'
description: 'The GameServer sample is the maintained, runnable example for Services, Kernels, Pushdown, disk plugin bundles, and the vendor server/client split.'
---
The GameServer sample is the maintained runnable example for **Services**, **Kernels**, and
**Pushdown**. It now models the product architecture used by real game integrations:

- A trusted **vendor game server** owns the world simulation and gold economy.
- A trusted **vendor game client** connects to the server over TCP, renders a console HUD, and hosts
  plugin client halves in its own in-process sandbox.
- A plugin is a disk bundle of analyzer-generated package JSON with separate `server/` and `client/`
  halves. The server installs privileged halves from disk under an operator allow-list; the client can
  only call approved server operations through a relay.

The sample lives under [`samples/GameServer`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer).
It is intentionally small, deterministic, and single-client, but it exercises the same trust boundaries
you need in a production host.

## Running It

Build or run the server from the repo root:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

The build exports plugin bundles into `samples/GameServer/plugins` through post-build targets in the
two authoring projects. The server then loads those packages from disk and launches the client process
for the one-command demo.

For a two-terminal run, start the server without launching the client:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj -- --listen 5555 --no-launch
```

Then connect the client:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Client/Examples.GameServer.Client.csproj -- --connect 127.0.0.1 5555 --plugins samples/GameServer/plugins
```

The TCP link is loopback-only in the sample. The server prints the explicit limitation:
real vendors put authenticated session identity and TLS on this link.

## What The Run Prints

The output is the easiest way to inspect the boundary:

1. **SERVER INSTALL** - the server loads bundle JSON, derives required capabilities from IR, intersects
   them with the operator allow-list, installs `guardian` and `bounty-hunter`, and denies the
   `gold-cheat` server half.
2. **BASELINE** - the deterministic world runs three ticks; `monster-1` dies and its bounty remains
   unclaimed.
3. **CONNECT** - the server listens on `127.0.0.1:<port>`, the client connects over TCP, and a scripted
   remote server-extension install attempt is refused because server halves install only from disk.
4. **CLIENT SANDBOX** - the client installs the `bounty-hunter` client halves, rejects the `gold-cheat`
   client half, renders the snapshot, and calls the bounty relay.
5. **WITH PLUGINS** - the client sees skull HUD frames for monster deaths, claims bounties through
   `bounty.claim`, sees duplicate/unknown-operation denials, and eventually hits the gold budget.
6. **OPERATOR RETUNE** - the server retunes `MaxBountyPerKill` on the installed bounty server half.
7. **SUMMARY** - balances, claimed monsters, and installed server kernel count are printed after the
   client disconnects.

Useful smoke markers are `listening on`, `client connected`, `bounty: paid`, and `=== SUMMARY ===`.

## Project Layout

| Project | Role |
| --- | --- |
| `Examples.GameServer.Server.Abstractions` | Shared server SDK and wire contracts: `IGameWorldAccess`, read-only `IGameWorldView`, gold ledger surface, events, and IPC control contracts. |
| `Examples.GameServer.Client.Abstractions` | Client-half plugin SDK: HUD, effects, server relay, and client-local events. It references no privileged gold bindings. |
| `Examples.GameServer.Server` | Trusted authoritative server: simulation, `GameEconomy`, bundle catalog, operator allow-list, TCP connection host, relay authorization, and policies. |
| `Examples.GameServer.Client` | Trusted vendor client: TCP connection, read-only world view, event feed callback, console renderer, and in-process client plugin sandbox. |
| `Examples.GameServer.Plugin` | Server-half authoring/export project: guardian/retaliation event kernels plus `BountyPayoutKernel` and the denied server-side `GoldCheatKernel`. |
| `Examples.GameServer.Plugin.Client` | Client-half authoring/export project: bounty claim UI, monster death FX, gold HUD, denied client cheat, and `skull.anim.txt`. |
| `Examples.GameServer.Plugin.Tests` | Focused sample tests for generated facade compatibility, routing, IPC, client relay policy, and package behavior. |

Some compatibility tests still exercise the older generated named-pipe facade. The maintained runnable
sample path is disk bundles plus the TCP vendor server/client split.

## Bundle Export

The exported bundle layout is:

```text
samples/GameServer/plugins/
  guardian/server/hooks/guardian.json
  guardian/server/subscriptions/retaliation.json
  bounty-hunter/server/extensions/bounty-payout.json
  bounty-hunter/client/extensions/bounty-claim.json
  bounty-hunter/client/hooks/monster-death-fx.json
  bounty-hunter/client/subscriptions/gold-hud.json
  bounty-hunter/client/assets/skull.anim.txt
  gold-cheat/server/extensions/gold-cheat.json
  gold-cheat/client/extensions/gold-cheat.json
```

The shared exporter lives in
[`Shared/PackageExporter.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Shared/PackageExporter.cs).
It resolves analyzer-generated packages with `KernelPackageRegistry.Resolve(type)` and writes
`PluginPackageJsonSerializer.Export(...)` output. The host still re-derives required capabilities from
the imported package; it does not trust the bundle folder name or a hand-written manifest.

## Server Install And Policy

The server catalog code lives under
[`Examples.GameServer.Server/PluginCatalog`](https://github.com/JKamsker/DotBoxD/tree/main/samples/GameServer/Examples.GameServer.Server/PluginCatalog).
`PluginBundleLoader` imports package JSON, `OperatorAllowList` is the sample operator artifact, and
`PluginCatalogInstaller` installs only allowed server parts.

`ServerPolicy` grants world capabilities only when the package actually requires them. Gold access is
split into read/write prefixes:

- `game.world.gold.read.*`
- `game.world.gold.write.*`

The `bounty-hunter` server half is allowed to grant bounded bounty payouts. The `gold-cheat` server
half also asks for `game.world.gold.write.grant`, but its bundle id is not allowed by the operator, so
the server denies it before installation.

## Gold Economy

The authoritative economy is in
[`Simulation/GameEconomy.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Simulation/GameEconomy.cs).
It owns balances, a treasury, claimed monster ids, and a per-tick payout budget. The kernel is stateless:
`BountyPayoutKernel` decides whether game rules allow a claim, then asks the ledger to grant the payout.
The ledger still refuses duplicate claims, budget overrun, or treasury overrun.

Gold changes publish `GoldChangedEvent`; monster deaths publish `MonsterKilledEvent`.

## Client Sandbox

The client SDK is intentionally narrower than the server SDK. `IGameClientAccess` exposes only:

- `game.client.ui.write`
- `game.client.fx.play`
- `game.client.server.call`

There is no gold ledger binding registered in the client sandbox. The client-side `gold-cheat` package
therefore cannot mutate gold directly, and the relay allow-list in
[`Sandbox/GameClientAccess.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Client/Sandbox/GameClientAccess.cs)
rejects non-approved operations before they hit the server control plane.

## TCP Link And Relay

[`Ipc/GameClientConnectionHost.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GameClientConnectionHost.cs)
starts a loopback `TcpServerTransport`, creates a per-connection `PluginSession`, provides
`IGameClientControlService`, and exposes only `IGameWorldView` to the client.

The relay entrypoint is `CallPluginOperationAsync` on
[`Ipc/GamePluginControlService.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginControlService.cs).
It enforces payload length, rate limit, operator-registered operation ids, and server-side player
identity injection before invoking the installed server extension. The client cannot claim to be a
different player in the payload.

Remote `InstallServerExtensionAsync` and `InvokeServerExtensionAsync` are refused on the client control
service. Server halves are installed only from disk after the operator allow-list check.

## Event Feed

The sample uses an explicit server-to-client feed in
[`Ipc/GameClientFeedForwarder.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GameClientFeedForwarder.cs).
It forwards selected server events through `IPluginEventCallback`; the client decodes them in
[`Feeds/ClientFeedCallback.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Client/Feeds/ClientFeedCallback.cs)
and republishes client-local events into the client sandbox.

Client-half kernels then render HUD output:

- `MonsterDeathFxKernel` sends `fx:skull:<monsterId>`, which plays `skull.anim.txt`.
- `GoldHudKernel` prints the player's gold balance.
- `BountyClaimKernel` calls the relay and prints the bounty receipt.

## Tests

Run the focused sample tests:

```bash
dotnet test samples/GameServer/Examples.GameServer.Plugin.Tests/Examples.GameServer.Plugin.Tests.csproj
```

Run the broader GameServer reflection and package tests:

```bash
dotnet test tests/DotBoxD.Kernels.Tests/DotBoxD.Kernels.Tests.csproj --filter FullyQualifiedName~Samples.GameServer
```

The docs smoke gate also checks that the exported bundle files exist and, on Windows CI, runs the
one-command server/client demo.

## See Also

- [Getting started](/getting-started/)
- [Concepts: services](/concepts/services/)
- [Concepts: kernels](/concepts/kernels/)
- [Concepts: pushdown](/concepts/pushdown/)
- [Tutorial: event pipelines (RunLocal)](/tutorials/event-pipeline-runlocal/)
- [Tutorial: Pushdown server extension](/tutorials/pushdown-server-extension/)
