# Kernel binding model: service interfaces vs hook chains (design round 3)

Companion to [plan.md](plan.md), [ownership-auth-and-policy.md](ownership-auth-and-policy.md),
[plugin-walkthrough.md](plugin-walkthrough.md), [server-walkthrough.md](server-walkthrough.md).

This round revisits **how a kernel is bound**. Round 1 bound kernels *inside* a hook chain
(`Hooks.On<MonsterAggroEvent>().UseKernel<GuardianKernel>()`). The feedback: a kernel is better
modelled as the implementation of a **server-published service contract**, registered by type — while
the hook chain stays for inline lambda logic.

> ⚠️ **Corrected by [implementation-plan.md](implementation-plan.md) (authoritative).** A self-review
> proved the §4 "generic wiring resolves a typed adapter from the event-**name** string" **cannot
> compile** (`HookRegistry.On` is generic-only; `TEvent` is erased through a string) — the real
> mechanism is an internal **shape-based** wiring path. Also: ship a **`Task`-returning**
> `Register(where:)` (the awaitable struct builder silently drops installs), and `ServiceContract` is a new
> **analyzer-emitted** manifest field. The current lowered terminal is `Run(lambda)`, with `RunLocal(lambda)`
> as the native escape hatch. The binding *concept* stands; these mechanism details follow the plan.

> **Grounding result that makes this cheap.** `PluginSymbolReader.EventTypes` walks
> `kernelType.AllInterfaces` ([PluginSymbolReader.cs](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginSymbolReader.cs)),
> so it detects `IEventKernel<TEvent>` **transitively**. A server interface
> `IMonsterAggroService : IEventKernel<MonsterAggroEvent>` that a kernel implements is therefore lowered
> **exactly as today — no analyzer change, same two-entrypoint (`ShouldHandle`/`Handle`) IR contract.**

---

## 1. Two complementary models

| | **Service kernel** (new, preferred for kernels) | **Hook chain** (unchanged) |
|---|---|---|
| Unit | a kernel *class* implementing a server contract | an inline lambda *pipeline* |
| API | `Setup(s => s.Hooks.On<TEvent>().Use<TKernel>())` / `Setup(s => s.Subscriptions.On<TEvent>().Use<TKernel>())` | `server.Hooks.On<TEvent>().Where/Select/Run/RunLocal` |
| Strong typing | full — server publishes `IMonsterAggroService` | by `TEvent` only |
| Lowered to IR | yes (kernel body) | yes (`Where`/`Select`/`Run` lambdas) |
| Native escape | n/a | `RunLocal` |
| Best for | reusable, named, settings-bearing behavior | ad-hoc filtering/projection, one-off handlers |

`UseKernel<TKernel>()` is a generated setup/registry operation, not a hook-chain terminal. `Use(InstalledKernel)`
survives as the **internal wiring primitive** that host-side wiring uses. The hook chain keeps exactly:
`Where | Select | Run | RunLocal`.

---

## 2. Server-published service contracts

The server (here, the game's shared abstractions) publishes a domain interface that **extends the
framework's `IEventKernel<TEvent>`** ([Contracts.cs](../../../src/Hosting/DotBoxD.Abstractions/Contracts.cs)):

```csharp
// DotBoxD.Kernels.Game.Server.Abstractions — the server owns the contract; both processes reference it.
public interface IMonsterAggroService : IEventKernel<MonsterAggroEvent> { }
public interface IAttackService        : IEventKernel<AttackEvent> { }
```

A plugin kernel implements the **contract**, not the bare framework interface:

```csharp
// DotBoxD.Kernels.Game.Plugin — note: implements IMonsterAggroService, not IEventKernel<MonsterAggroEvent>.
[Plugin("guardian")]
public sealed partial class GuardianKernel : IMonsterAggroService
{
    [LiveSetting] [Range(0, 100)] public int LevelGap { get; set; } = 3;
    // … live settings …
    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx) => /* gate */;
    public void Handle(MonsterAggroEvent e, HookContext ctx) => ctx.Messages.Send(/* … */);
}
```

Why this is better than binding a bare kernel into a hook:
- **Domain naming.** "implements `IMonsterAggroService`" says what the kernel *is*, not just which
  event it reads.
- **Typed wiring, no string switch.** Today the server hand-maps a manifest subscription string to an
  adapter in a `switch` ([GamePluginControlService.cs](../../../samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginControlService.cs)).
  The contract carries `TEvent` in its type, so wiring is generic (§4) and that switch is **deleted**.
- **Server-side typed access.** The server references the contract (it published it), so a contract-indexed
  kernel lookup remains meaningful even though the server never sees the plugin's kernel type (§5).

> MVP keeps a service interface as a **named `IEventKernel<TEvent>`** (zero extra methods) so the
> two-entrypoint IR contract is untouched. Richer service shapes (multiple methods, non-event returns)
> are a future extension needing analyzer work — explicitly out of scope here.

---

## 3. Generated setup with an optional lowered gate

```csharp
[GeneratePluginServer(Context = typeof(GamePluginContext))]
public partial class GamePluginServer : IGameWorldAccess;

public sealed partial class GamePluginContext;
```

Usage — plain, and with the optional per-event gate ("for which user does it apply?"):

```csharp
using var server = GamePluginServerBuilder
    .FromPipeName(pipeName)
    .Setup(s =>
    {
        s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>();
        s.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>();
    })
    .Build();
```

Any gate is plugin code, so it must lower into verified IR and compose with the kernel's own gate; it is never
native.

### 3.1 What setup does across the two processes

- **Plugin side (the generated facade).** `Setup(...)` records `TKernel`'s analyzer-generated package
  (via `KernelPackageRegistry.Resolve<TKernel>()`) and `StartAsync()` ships the verified IR over IPC.
  The `[Plugin]` id and the manifest's event subscription (derived from `IEventKernel<TEvent>`,
  transitively) ride along.
- **Server side.** Installs the IR through the owning **session** (so the kernel is owned and revoked
  on disconnect — [ownership-auth-and-policy.md](ownership-auth-and-policy.md) §2) under the **resolved
  per-plugin policy** (§4 there), then wires it generically (§4 below). The optional `Where` gate is
  part of the shipped IR, so the server needs no extra wire payload for it.

---

## 4. Wiring becomes generic (the `switch` dies)

Round-1 wiring hardcoded the event→adapter mapping:

```csharp
// GamePluginControlService.WireHook (today) — hand-maintained per event.
switch (subscription) {
    case "MonsterAggroEvent": _server.Hooks.On(MonsterAggroEventAdapter.Instance).UseKernel(kernel); break;
    case "AttackEvent":       _server.Hooks.On(AttackEventAdapter.Instance).UseKernel(kernel); break;
    …
}
```

With (a) **convention adapters** ([ownership-auth-and-policy.md](ownership-auth-and-policy.md) §3 — the
server resolves an adapter by event name, no hand-written adapter) and (b) the manifest's event name,
wiring is a single generic path:

```csharp
// Generic: resolve adapter by the manifest's event name, then wire via the internal primitive.
var adapter = server.Events.Resolve<AttackEvent>();
server.Hooks.On(adapter).Use(kernel);   // Use(InstalledKernel) = internal wiring primitive
```

No per-event `case`, no hand-written adapter, no kernel-id assumptions. Adding a new service contract
(`IShopPurchaseService : IEventKernel<PurchaseEvent>`) needs **no server wiring code** — only the event
record in the shared abstractions.

> **Manifest addition.** To support server-side `Get<TService>()` (§5), the manifest records an
> optional `ServiceContract` (the interface's full name) so the server can index installed kernels by
> contract. Wiring itself still keys off the **event** (already in the manifest), so the contract name
> is metadata, not load-bearing for dispatch.

---

## 5. Typed access: `Get<TKernel>()` vs `Get<TService>()` vs `Get(string)`

The "`Get<MyKernel>()` or `IMonsterAggroService`?" question resolves cleanly once you notice **which
side has which type**:

| Getter | Plugin side | Server side | Ambiguity |
|---|---|---|---|
| `Get<TKernel>()` | ✅ natural — you authored the kernel type | ✗ server never sees the kernel type (opaque IR) | none — one `[Plugin]` id per class |
| `Get<TService>()` | ✅ works | ✅ **the** server-side getter — server published the contract | **yes** if several kernels implement `TService` (e.g. per-user) |
| `Get(string pluginId)` | ✅ | ✅ | none |

Recommendations:
- **Plugin/author code:** prefer `Get<TKernel>()` — unambiguous, strongly typed, resolves to the
  `[Plugin]` id via `KernelTypeMetadata.PluginId` ([already exists, internal](../../../src/Hosting/DotBoxD.Plugins/Runtime/KernelTypeMetadata.cs)).
  The generated facade exposes `Get<TKernel>()` for plugin-authored live settings.
- **Server code:** use `Get<TService>()`; throw on ambiguity and add `GetAll<TService>()` for the
  many-kernels case (per-user registrations).
- **Either side, dynamic:** `Get(string)` stays.

### 5.1 `Set(...).ApplyAsync` — strongly-typed live settings

```csharp
// Plugin side — typed, replaces the string .Set("CalmStrength", 35) chain.
await server.Get<GuardianKernel>()
    .Set(k => k.CalmStrength, 35)
    .Set(k => k.AggroRange, 6)
    .ApplyAsync(atomic: true);
```

This is the generated facade's typed settings handle over the runtime live-settings machinery
([TypedInstalledKernel.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/TypedInstalledKernel.cs)). The
author selects `[LiveSetting]` members with expressions, queues values with `Set`, and `ApplyAsync` ships the
changed values (`LiveKernelValueFactory.ExtractSettings`) to the server.

> **Cross-process subtlety (flag for critique).** On the plugin side the lambda runs on a *local draft*
> (built from declared defaults / last-pushed values), then ships the resulting values over IPC
> (`UpdateSettingsAsync`). So it is for **setting** values (`k.X = …`), not read-modify-write against
> live server state — the plugin may not hold the current server value. For atomic read-modify-write,
> use the **server-side** `Get(id).Set(...).ApplyAsync(atomic: true)`, which mutates against live state
> under the kernel's execution gate. (Open question §7.3.)

---

## 6. How the example reads now

```csharp
using var server = GamePluginServerBuilder
    .FromPipeName(pipeName)
    .Setup(s =>
    {
        s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>();
        s.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>();
    })
    .Build();

await server.Get<GuardianKernel>()
    .Set(k => k.CalmStrength, 35)
    .Set(k => k.AggroRange, 6)
    .ApplyAsync(atomic: true);
```

```csharp
// PLUGIN — hook chain still available for inline lambda logic (no kernel class)
server.Hooks.On<AttackEvent>()
    .Where(e => e.AttackerLevel >= 5)                   // lowered
    .Select(e => e.AttackerId)                          // lowered projection
    .Run((attackerId, ctx) => ctx.Messages.Send(attackerId, "taunt"));  // lowered terminal

server.Subscriptions.On<AttackEvent>()
    .RunLocal((e, ctx) => { Telemetry.Count("attack"); return ValueTask.CompletedTask; }); // native
```

---

## 7. Open questions surfaced for the critique panel

1. **`Get<TService>()` ambiguity** (§5): throw + `GetAll<TService>()`, or make `Get<TService>()` return
   a collection, or only support `Get<TKernel>()`/`Get(string)` and drop service-typed get?
2. **Plugin-side `Set(...).ApplyAsync` semantics** (§5.1): is "set-only against a local draft" acceptable, or
   do we need a read-modify-write round-trip (fetch current → apply → push) for the plugin side?
3. **Manifest `ServiceContract` field** (§4): worth adding to enable server-side `Get<TService>()`, or
   YAGNI — wire purely by event and look kernels up by id?
4. **Service interface as bare marker** (§2): is `interface IFooService : IEventKernel<TEvent> {}` with
   no members too thin to justify the concept, or is the naming/typing payoff enough?
