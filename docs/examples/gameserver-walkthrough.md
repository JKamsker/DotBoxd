# Example: the GameServer sample, end to end

The GameServer sample is the maintained, runnable example that ties **Services**, **Kernels**, and **Pushdown** together in one program. It is the canonical reference because it exercises all three modes end to end in a single process pair:

- **Services (RPC)** — typed interop from one C# contract.
- **Query/event pipeline (RunLocal)** — server-side filtering and projection so the host receives only the data it needs.
- **Pushdown** — server extensions that batch work next to the data instead of round-tripping.

So the patterns you see here map straight onto your own host and plugins. A parent *server* process runs a small deterministic simulation; a child *plugin* process ships untrusted, sandboxed kernels to the server over a bidirectional named-pipe control plane. This page walks the sample feature by feature and maps each one to the concrete file that implements it, so you can jump straight into the real code.

The sample lives under [`samples/GameServer`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer) and is the example the root [`README.md`](https://github.com/JKamsker/DotBoxD/blob/main/README.md) points at for "service IPC, event kernels, live settings, host bindings, policies, and server extensions."

## Running it

One command builds and runs everything. The server launches the plugin child process for you (see [`Examples.GameServer.Server/Ipc/PluginLauncher.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/PluginLauncher.cs)), so you do not start the plugin separately:

```bash
dotnet run -c Release --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

The server accepts one optional argument, `--use-builder`, which selects the fluent builder entrypoint on the plugin side; without it the plugin uses the same generated server through its default path. The argument is validated in [`Examples.GameServer.Server/Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Program.cs):

```csharp
if (args.Length > 1 || (args.Length == 1 && args[0] != "--use-builder"))
{
    await Console.Error
        .WriteLineAsync("Usage: Examples.GameServer.Server [--use-builder]")
        .ConfigureAwait(false);
    return 1;
}
```

### What the run prints

`Program.Main` runs three phases so the effect of the plugin is visible:

1. **Baseline** — no plugins; lvl-8 monsters bully the low-level players for a few ticks.
2. **With plugins** — the plugin connects, installs its kernels, and the same simulation now shows guardian/retaliation effects.
3. **Summary** — per-tick damage before vs. after, plus proof that disconnect unloaded the plugin's kernels.

The phase structure is driven by `BaselineTicks` / `PluginTicks` and the `world.TickAsync()` loop in `Program.cs`.

## Project layout

| Project | Role | Key files |
| --- | --- | --- |
| `Examples.GameServer.Server` | The host/parent process: the 1D simulation, the IPC control plane, host bindings, and policies. | `Program.cs`, `ServerPolicy.cs`, `Ipc/`, `Simulation/` |
| `Examples.GameServer.Server.Abstractions` | The shared contracts both sides compile against: the domain surface, events, IPC service interfaces, and the command DSL. | `IGameWorldAccess.cs`, `Events/`, `Ipc/`, `ServiceContracts.cs`, `GameCommands.cs` |
| `Examples.GameServer.Plugin` | The untrusted plugin/child process: authored-in-C# kernels, server extensions, and the one-line generated plugin server. | `Program.cs`, `GamePluginServer.cs`, `Kernels/` |
| `Examples.GameServer.Plugin.Tests` | xUnit coverage that exercises the plugin surface, IPC round-trips, and server-extension RPC without spawning processes. | `Regression/`, `RunLocal/`, `Routing/` |

Both `Server` and `Plugin` reference the `Server.Abstractions` project (see the two `.csproj` files); the plugin additionally references the analyzer as an `Analyzer` output so its C# kernels are lowered to verified IR at build time ([`Examples.GameServer.Plugin.csproj`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Examples.GameServer.Plugin.csproj)).

## The shared contract (Server.Abstractions)

Everything hinges on **one pure domain interface** that has three consumers: the server implements it, the plugin gets an RPC proxy of it, and a kernel gets it injected. From [`IGameWorldAccess.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/IGameWorldAccess.cs):

```csharp
[DotBoxDService]
public interface IGameWorldAccess
{
    /// <summary>Monster-specific commands and scoped monster handles exposed by the game world.</summary>
    IMonsterControl Monsters { get; }

    /// <summary>Entity-wide commands and scoped entity handles exposed by the game world.</summary>
    IEntityControl Entities { get; }
}
```

Each method carries the capability and host-state effect it needs as metadata, so the analyzer and the runtime consume the same source of truth. For example, `IMonster.KillAsync` on the same file:

```csharp
/// <summary>Kills this monster and returns whether the world changed.</summary>
[HostCapability("game.world.monster.write.kill", HostBindingEffect.HostStateWrite)]
ValueTask<bool> KillAsync();
```

`[HostCapability]` here is the *auto-binding* form (the source calls these "analyzer-visible auto bindings"): on a `[DotBoxDService]` domain-interface method you declare only the capability and its `HostBindingEffect`, and the framework derives the binding from the interface method — whereas the explicit [`[HostBinding("id", "cap", SandboxEffect)]`](../tutorials/pushdown-server-extension.md) you met in Pushdown Step 2 (the [glossary](../reference/glossary.md)'s *Host binding*) makes you pin the binding id yourself and declares its effects with a different enum, `SandboxEffect` rather than `HostBindingEffect`.

Supporting contracts in this project:

- **Events** — [`Events/MonsterAggroEvent.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/Events/MonsterAggroEvent.cs) and [`Events/AttackEvent.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/Events/AttackEvent.cs). Both are plain records; the framework infers the sandbox event shape from their properties. `AttackEvent` marks `AttackerId`, `TargetId`, and `Damage` with `[EventIndexKey]` so lowered `.Where(...)` predicates can be prefiltered through host dispatch indexes.
- **Named service contracts** — [`ServiceContracts.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/ServiceContracts.cs) exposes `IMonsterAggroService : IEventKernel<MonsterAggroEvent>` and `IAttackService : IEventKernel<AttackEvent>`, letting a kernel declare its behavior as a named domain service.
- **The IPC control plane** — [`Ipc/IGamePluginControlService.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/Ipc/IGamePluginControlService.cs) (install IR, update settings, hold the connection) and the reverse-direction [`Ipc/IPluginEventCallback.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/Ipc/IPluginEventCallback.cs) (server → plugin push for remote `RunLocal` chains). Both are `[DotBoxDService]`.
- **The command DSL** — [`GameCommands.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server.Abstractions/GameCommands.cs) defines what a kernel's `host.message.write` messages *mean* (`calm:<player>:<strength>`, `taunt:<target>`); this meaning is defined in the example, never in the DotBoxD core.

## The server (parent process)

The server builds the world and the plugin server, then drives phases. From [`Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Program.cs):

```csharp
var sink = new GameCommandSink();
var worldHost = new GameWorldHost();
using var server = PluginServer.Create(
    sink,
    configureHost: worldHost.AddBindings,
    defaultPolicy: ServerPolicy.Create(),
    executionMode: ExecutionMode.Compiled);
```

Three things are wired here: the **command sink** (the host capability that turns plugin messages into state changes), the **host bindings** for gated `IGameWorldAccess` reads, and the **default sandbox policy**. `ExecutionMode.Compiled` selects the compiled kernel backend.

The simulation itself is a deterministic 1D line of players and monsters ([`Simulation/GameWorld.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Simulation/GameWorld.cs)). Each tick it publishes `MonsterAggroEvent` through hooks and `AttackEvent` through subscriptions (and the index registry), so plugin kernels get a chance to react before damage lands.

The `host.message.write` capability is realized by [`Simulation/GameCommandSink.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Simulation/GameCommandSink.cs), which parses the DSL, validates it (known verb, real monster/player ids, clamped strength), and applies it — invalid commands are ignored safely and never throw back into the sandbox.

## The plugin control-plane service IPC

The per-connection IPC ceremony is wrapped by the framework's `PluginConnectionHost<TConnection>`; the sample only supplies the connection-specific work in [`Ipc/GamePluginHost.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginHost.cs):

```csharp
public static Task<PluginConnectionHost<GamePluginControlService>> StartAsync(
    PluginServer server,
    GameCommandSink sink,
    GameWorld world)
    => PluginConnectionHost<GamePluginControlService>.StartAsync(
        server,
        "dotboxd-game-" + Guid.NewGuid().ToString("N"),
        (peer, session) =>
        {
```

Two `[DotBoxDService]` implementations are provided per connection (the control plane and the world surface), and the reverse `IPluginEventCallback` proxy is fetched from the peer.

The control-plane implementation is [`Ipc/GamePluginControlService.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginControlService.cs). It never sees kernel *source* — the plugin ships opaque verified IR as `packageJson`, and the service installs and wires it through its owning `PluginSession`:

```csharp
var package = PluginPackageJsonSerializer.Import(packageJson);
Console.WriteLine($"[server] installing plugin kernel '{package.Manifest.PluginId}'...");
var kernel = await _session.InstallAndWireAsync(
    package,
    _kernelWiring.WireHook,
    policy: pkg => ServerPolicy.ForKernel(_server.GetRequiredCapabilities(pkg)),
    validate: _kernelWiring.ValidateRoute,
    ct).ConfigureAwait(false);
```

The host's wiring *policy* — which events this server supports, how terminals route, and which callbacks/index to attach — lives in [`Ipc/GamePluginKernelWiring.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginKernelWiring.cs). It registers the supported event adapters so the framework router can resolve a kernel's subscribed event *by name*.

On the plugin side, the whole facade is one partial class ([`GamePluginServer.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/GamePluginServer.cs)):

```csharp
[GeneratePluginServer(Context = typeof(GamePluginContext))]
public partial class GamePluginServer : IGameWorldAccess;
```

The generator emits the RPC proxy, the `StartAsync`/`HoldUntilShutdownAsync` lifecycle, the `Setup` install accumulator, live settings, and the `GamePluginServerBuilder` — all from `: IGameWorldAccess`.

## Event kernels

Kernels are authored as ordinary C# and lowered to verified IR by the analyzer. The plugin records them at build time in [`Examples.GameServer.Plugin/Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Program.cs):

```csharp
.Setup(s =>
{
    // Build() is sync and does no I/O; StartAsync() ships the recorded IR.
    s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>();
    s.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>();
```

- **`GuardianKernel`** ([`Kernels/GuardianKernel.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Kernels/GuardianKernel.cs)) is a *hook* (awaited decision) on `MonsterAggroEvent`. It calms a monster that is about to bully a low-level player. Its `[EventKernel]` install id derives from the type name (`"guardian"`) — nothing is hand-typed.
- **`RetaliationKernel`** ([`Kernels/RetaliationKernel.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Kernels/RetaliationKernel.cs)) is a fire-and-forget *subscription* on `AttackEvent` that taunts a strong attacker away.

`GuardianKernel` also factors its gate into a reusable, unit-testable `[KernelMethod]` that the generator inlines:

```csharp
public void Handle(MonsterAggroEvent e, HookContext ctx)
    => ctx.Messages.Send(e.MonsterId, $"calm:{e.PlayerId}:{CalmStrength}");
```

After `StartAsync()`, the plugin also installs **inline remote chains** (`ConfigureRuntimeHooks` in `Program.cs`), whose `Where`/`Select`/`Run` are lowered to verified IR — including an indexed subscription whose two `.Where` leaves compare `[EventIndexKey]` fields to constants:

```csharp
server.Subscriptions.On<AttackEvent>()
    .Where(e => e.AttackerId == "player-1" && e.Damage >= 5)
    .Select(e => e.TargetId)
    .Run((targetId, ctx) => ctx.Messages.Send(targetId, "indexed-taunt:inline"));
```

## Live settings

Members marked `[LiveSetting]` (with validation such as `[Range(0, 100)]`) can be re-tuned on an installed kernel without reinstalling it. From [`Examples.GameServer.Plugin/Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Program.cs):

```csharp
await server.Get<GuardianKernel>()
    .Set(k => k.CalmStrength, 35)
    .Set(k => k.AggroRange, 6)
    .ApplyAsync(atomic: true);
```

The setters are strongly typed member expressions; only `[LiveSetting]` members are settable, and `ApplyAsync` ships the batch. On the server this arrives as `UpdateSettingsAsync` on the control service, which the owning session applies (rejecting ids it does not own).

## Host bindings

The server's real implementation of the domain surface is [`Ipc/GameWorldAccess.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GameWorldAccess.cs). Its calls are synchronous against the in-process world, returned as completed `ValueTask`s — the async shape exists only so the remote proxy and in-sandbox kernels share one contract. `Get(id)` returns a scoped handle that captures the id, and each method carries the same `[HostCapability]` metadata as the SDK contract:

```csharp
[HostCapability("game.world.monster.write.kill", HostBindingEffect.HostStateWrite)]
public ValueTask<bool> KillAsync()
    => ValueTask.FromResult(_world().KillMonster(Id));
```

These bindings are registered onto the host by `GameWorldHost.AddBindings`, passed to `PluginServer.Create(configureHost: ...)` in the server's `Program.cs`.

## Policy-gated execution (ServerPolicy)

Every kernel gets a **least-privilege** sandbox policy computed from what server-side package analysis says its verified IR actually needs. From [`ServerPolicy.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/ServerPolicy.cs):

```csharp
var builder = SandboxPolicyBuilder.Create()
    .GrantLogging()
    .GrantHostMessageWrite()
    .WithFuel(100_000)
    .WithMaxHostCalls(1_000);

if (RequiresPrefix(requiredCapabilities, MonsterReadPrefix))
{
    builder.Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead);
}
```

The control service feeds this with `_server.GetRequiredCapabilities(pkg)` at install time, so a kernel that never declares a monster-write binding (the retaliation kernel) is never over-granted, and a kernel missing even the `host.message.write` grant fails closed during package preparation.

## The server extension (pushdown)

A **server extension** is a kernel that runs on the *server* but is authored, shipped, and owned by the plugin — the pushdown story. Instead of round-tripping many small RPC reads, the plugin grafts a method onto the domain surface and the server executes it locally against the world. The plugin records extensions in `Setup`:

```csharp
s.Monsters.Extend<MonsterKillerKernel>();        // grafts onto IMonsterControl (batch)
s.Monsters.Extend<RangeMonsterKillerKernel>();   // batch with a value-object query parameter
s.Monsters.Extend<BlinkKernel>();                // grafts onto IMonster handles (per-instance)
```

- **`MonsterKillerKernel`** ([`Kernels/MonsterKillerKernel.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Kernels/MonsterKillerKernel.cs)) is a `[ServerExtension(typeof(IMonsterControl))]` **batch** grafted onto the collection. It is injected the same `IGameWorldAccess` the plugin uses remotely — but because it runs on the server, the awaited reads/writes are local (no real IPC hop):

  ```csharp
  [ServerExtensionMethod]   // grafted as IMonsterControl.KillMonstersAsync (name = the method's name)
  public async ValueTask<List<MonsterKillResult>> KillMonstersAsync(List<string> monsterIds, HookContext ctx)
  {
      var results = new List<MonsterKillResult>();
      foreach (var id in monsterIds)
      {
          var monster = _world.Monsters.Get(id);            // scoped handle — id captured once
          var healthBefore = await monster.GetHealthAsync();
  ```

- **`RangeMonsterKillerKernel`** ([`Kernels/RangeMonsterKillerKernel.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Kernels/RangeMonsterKillerKernel.cs)) is the same batch shape but takes one `WorldRangeQuery` value object (with a nested `WorldPoint`) instead of loose primitives, exercising record/DTO parameter marshalling on an extension entrypoint.
- **`BlinkKernel`** ([`Kernels/BlinkKernel.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/Kernels/BlinkKernel.cs)) is a `[ServerExtension(typeof(IMonster))]` **per-instance** extension. When the caller does `Monsters.Get("monster-4").BlinkBehindAsync(...)`, the `Get(id)` captures the id, the server resolves that monster and injects it, and the body uses `_monster` directly:

  ```csharp
  public BlinkKernel(IMonster monster, IGameWorldAccess world)
  {
      _monster = monster;
      _world = world;
  }
  ```

On the wire, the control service installs an extension via `InstallServerExtensionAsync` and invokes it via `InvokeServerExtensionAsync`, with ownership checked atomically before dispatch ([`GamePluginControlService.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginControlService.cs)):

```csharp
if (!_session.TryGetOwned(pluginId, out var kernel))
{
    throw new InvalidOperationException(
        $"Server extension '{pluginId}' is not owned by this plugin session.");
}

return await kernel.InvokeServerExtensionRpcAsync(arguments, ct).ConfigureAwait(false);
```

The plugin calls these grafted methods as if they were part of the world surface ([`AdvancedUsage.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin/AdvancedUsage.cs)):

```csharp
var killResults = await server.Monsters.KillMonstersAsync(["monster-3", "monster-4", "player-1"]);
```

`AdvancedUsage.cs` also demonstrates the throwaway `InvokeAsync` probe overloads (single-lambda read, and the explicit capture-bag for write-back).

## Unload on disconnect

Kernel lifetime is tied to the connection. When the server's with-plugin phase finishes it signals shutdown; the plugin releases its `HoldUntilShutdownAsync` and disconnects. From [`Examples.GameServer.Server/Program.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Program.cs):

```csharp
// (g) Release the plugin; it disconnects, and ownership unloads its kernels.
control.SignalShutdown();
```

`PluginConnectionHost` disposes the per-peer `PluginSession` on disconnect, and the session unloads every kernel it owned. The summary proves it by reporting the live kernel count, which returns to zero:

```csharp
Console.WriteLine($"On disconnect the plugin's kernels were unloaded (installed kernels now: {server.Kernels.Snapshot().Count}).");
```

The connect/ready/shutdown handshake is fail-fast: [`Ipc/PluginReadinessGate.cs`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Server/Ipc/PluginReadinessGate.cs) races readiness against the plugin's early exit and a timeout so a plugin that never connects or crashes on install does not hang the server.

## How the sample is exercised (Plugin.Tests)

[`Examples.GameServer.Plugin.Tests`](https://github.com/JKamsker/DotBoxD/blob/main/samples/GameServer/Examples.GameServer.Plugin.Tests) is an xUnit project that drives the plugin surface in-process, without spawning the two executables:

- **Server-extension RPC** — `Regression/MonsterKillerServerExtensionRegressionTests.cs` installs `MonsterKillerKernel` into a real `PluginServer`, encodes arguments with `KernelRpcBinaryCodec`, calls `InvokeServerExtensionRpcAsync`, and asserts the returned list of record-structs.
- **IPC round-trips** — `Regression/GamePluginControlServiceIpcRegressionTests.cs` stands up the generated named-pipe service (`RpcMessagePackIpc.ListenNamedPipe`) and proves the inherited `InvokeServerExtensionAsync` wire method round-trips.
- **Builder, routing, RunLocal, and server context** — `RemotePluginServerBuilder*Tests.cs`, `Routing/RouterParityTests.cs`, `RunLocal/RemoteRunLocalFacadeIpcTests.cs`, and `ServerContext/RemoteServerContextTests.cs` cover the generated builder, router parity, remote `RunLocal` chains, and the server context surface.

Run them with:

```bash
dotnet test samples/GameServer/Examples.GameServer.Plugin.Tests/Examples.GameServer.Plugin.Tests.csproj
```

## See also

Build the same pieces from scratch:

- [Tutorial: your first Service](../tutorials/first-service.md)
- [Tutorial: event pipelines (RunLocal)](../tutorials/event-pipeline-runlocal.md)
- [Tutorial: Pushdown server extension](../tutorials/pushdown-server-extension.md)

Or go deeper on the concepts:

- [Getting started](../getting-started/README.md)
- [Concepts: services](../concepts/services.md)
- [Concepts: kernels](../concepts/kernels.md)
- [Concepts: pushdown](../concepts/pushdown.md)
