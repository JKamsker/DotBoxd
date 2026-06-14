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
> `Register(where:)` (the awaitable struct builder silently drops installs), `ServiceContract` is a new
> **analyzer-emitted** manifest field, and `InvokeKernel(lambda)` is a Phase-C terminal (today it is
> `[Obsolete(error:true)]`). The binding *concept* stands; these mechanism details follow the plan.

> **Grounding result that makes this cheap.** `PluginSymbolReader.EventTypes` walks
> `kernelType.AllInterfaces` ([PluginSymbolReader.cs:26-39](../../../src/DotBoxd.Plugins.Analyzer/Analysis/PluginSymbolReader.cs)),
> so it detects `IEventKernel<TEvent>` **transitively**. A server interface
> `IMonsterAggroService : IEventKernel<MonsterAggroEvent>` that a kernel implements is therefore lowered
> **exactly as today — no analyzer change, same two-entrypoint (`ShouldHandle`/`Handle`) IR contract.**

---

## 1. Two complementary models

| | **Service kernel** (new, preferred for kernels) | **Hook chain** (unchanged) |
|---|---|---|
| Unit | a kernel *class* implementing a server contract | an inline lambda *pipeline* |
| API | `server.Kernels.Register<TService, TKernel>()` | `server.Hooks.On<TEvent>().Where/Select/InvokeKernel/InvokeLocal` |
| Strong typing | full — server publishes `IMonsterAggroService` | by `TEvent` only |
| Lowered to IR | yes (kernel body) | yes (`Where/Select/InvokeKernel` lambdas) |
| Native escape | n/a | `InvokeLocal` |
| Best for | reusable, named, settings-bearing behavior | ad-hoc filtering/projection, one-off handlers |

`UseKernel<TKernel>()` **leaves the public hook-chain surface** — its job (bind a whole kernel class)
moves to `Kernels.Register`. `UseKernel(InstalledKernel)` survives only as the **internal wiring
primitive** that `Register` uses. The hook chain keeps exactly what the request listed:
`Where | Select | InvokeKernel | InvokeLocal`.

---

## 2. Server-published service contracts

The server (here, the game's shared abstractions) publishes a domain interface that **extends the
framework's `IEventKernel<TEvent>`** ([Contracts.cs:14-19](../../../src/DotBoxd.Abstractions/Contracts.cs)):

```csharp
// DotBoxd.Kernels.Game.Server.Abstractions — the server owns the contract; both processes reference it.
public interface IMonsterAggroService : IEventKernel<MonsterAggroEvent> { }
public interface IAttackService        : IEventKernel<AttackEvent> { }
```

A plugin kernel implements the **contract**, not the bare framework interface:

```csharp
// DotBoxd.Kernels.Game.Plugin — note: implements IMonsterAggroService, not IEventKernel<MonsterAggroEvent>.
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
  adapter in a `switch` ([GamePluginControlService.cs:59-77](../../../examples/GameServer/DotBoxd.Kernels.Game.Server/Ipc/GamePluginControlService.cs)).
  The contract carries `TEvent` in its type, so wiring is generic (§4) and that switch is **deleted**.
- **Server-side typed access.** The server references the contract (it published it), so
  `server.Kernels.Get<IMonsterAggroService>()` is meaningful even though it never sees the kernel type
  (§5).

> MVP keeps a service interface as a **named `IEventKernel<TEvent>`** (zero extra methods) so the
> two-entrypoint IR contract is untouched. Richer service shapes (multiple methods, non-event returns)
> are a future extension needing analyzer work — explicitly out of scope here.

---

## 3. `Kernels.Register<TService, TKernel>()` with an optional lowered gate

```csharp
public ServiceKernelRegistration<TService> Register<TService, TKernel>()
    where TService : class
    where TKernel  : class, TService;   // the kernel must implement the published contract
```

Usage — plain, and with the optional per-event gate ("for which user does it apply?"):

```csharp
await server.Kernels.Register<IMonsterAggroService, GuardianKernel>();

await server.Kernels.Register<IAttackService, RetaliationKernel>()
    .Where((e, ctx) => e.Damage >= 10);          // lowered to verified IR, runs before the kernel gate
```

`ServiceKernelRegistration<TService>` is an **awaitable builder**: `.Where(..)` accumulates lowered
gates and the value is awaitable directly (its `GetAwaiter` forwards to `ApplyAsync()`), so both forms
above `await` cleanly. This keeps **one way to express a gate** — a fluent `Where`, lowered and
sandboxed, identical to the hook-chain `Where` ([ownership-auth-and-policy.md](ownership-auth-and-policy.md) §1).

> **Design choice flagged for critique.** An awaitable fluent builder (custom `GetAwaiter`) is
> ergonomic but slightly surprising. The fallback is an optional parameter:
> `Register<TService, TKernel>(where: (e, ctx) => …)`. Proposal: ship the awaitable builder; it keeps
> the gate fluent and consistent. (Open question §7.1.)

The gate is plugin code → **lowered and AND-composed into the kernel's gate at compile time** (same
mechanism as §1 of the round-2 doc); it is never native.

### 3.1 What `Register` does across the two processes

- **Plugin side (the shim).** Resolves `TKernel`'s analyzer-generated package (via the
  `KernelPackageRegistry` self-registration), ships the verified IR over IPC. The `[Plugin]` id and the
  manifest's event subscription (derived from `IEventKernel<TEvent>`, transitively) ride along.
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
var adapter = server.Events.ResolveByEventName(kernel.Manifest.Subscriptions[0].Event);
server.Hooks.On(adapter).UseKernel(kernel);   // UseKernel(InstalledKernel) = internal wiring primitive
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
  `[Plugin]` id via `KernelTypeMetadata.PluginId` ([already exists, internal](../../../src/DotBoxd.Plugins/Runtime/KernelTypeMetadata.cs)).
  Promote the existing internal `GetByKernelType<TKernel>()` ([PluginServer.cs:168-172](../../../src/DotBoxd.Plugins/PluginServer.cs)) to public.
- **Server code:** use `Get<TService>()`; throw on ambiguity and add `GetAll<TService>()` for the
  many-kernels case (per-user registrations).
- **Either side, dynamic:** `Get(string)` stays.

### 5.1 `SetValuesAsync` — strongly-typed live settings

```csharp
// Plugin side — typed, replaces the string .Set("CalmStrength", 35) chain.
await server.Kernels.Get<GuardianKernel>()
    .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true);
```

This is `TypedInstalledKernel<T>.ModifyAsync(Action<T>, atomic, ct)` **[already exists]**
([TypedInstalledKernel.cs:25-29](../../../src/DotBoxd.Plugins/Runtime/TypedInstalledKernel.cs)),
renamed to the clearer `SetValuesAsync`. The lambda mutates a typed **draft** of the kernel's
`[LiveSetting]` properties; the framework extracts the changed values
(`LiveKernelValueFactory.ExtractSettings`) and applies them.

> **Cross-process subtlety (flag for critique).** On the plugin side the lambda runs on a *local draft*
> (built from declared defaults / last-pushed values), then ships the resulting values over IPC
> (`UpdateSettingsAsync`). So it is for **setting** values (`k.X = …`), not read-modify-write against
> live server state — the plugin may not hold the current server value. For atomic read-modify-write,
> use the **server-side** `Get(id).SetValuesAsync(…, atomic: true)`, which mutates against live state
> under the kernel's execution gate. (Open question §7.3.)

---

## 6. How the example reads now

```csharp
// PLUGIN — Program.Main (service-kernel model)
var server = new RemotePluginServer(connection.Get<IGamePluginControlService>());

await server.Kernels.Register<IMonsterAggroService, GuardianKernel>();
await server.Kernels.Register<IAttackService, RetaliationKernel>()
    .Where((e, ctx) => e.Damage >= 10);                // optional lowered gate

await server.Kernels.Get<GuardianKernel>()
    .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true);
```

```csharp
// PLUGIN — hook chain still available for inline lambda logic (no kernel class)
server.Hooks.On<AttackEvent>()
    .Where((e, ctx) => e.AttackerLevel >= 5)            // lowered
    .Select((e, ctx) => e.AttackerId)                   // lowered projection
    .InvokeKernel((attackerId, ctx) => ctx.Messages.Send(attackerId, "taunt"));  // lowered terminal

server.Events.On<AttackEvent>()
    .InvokeLocal((e, ctx) => { Telemetry.Count("attack"); return ValueTask.CompletedTask; }); // native
```

---

## 7. Open questions surfaced for the critique panel

1. **Awaitable builder vs `where:` parameter** (§3): ship `Register<…>().Where(..)` as an awaitable
   fluent builder, or a plain `Register<…>(where: …)` parameter? (Awaitable builders can surprise.)
2. **`Get<TService>()` ambiguity** (§5): throw + `GetAll<TService>()`, or make `Get<TService>()` return
   a collection, or only support `Get<TKernel>()`/`Get(string)` and drop service-typed get?
3. **Plugin-side `SetValuesAsync` semantics** (§5.1): is "set-only against a local draft" acceptable, or
   do we need a read-modify-write round-trip (fetch current → apply → push) for the plugin side?
4. **Manifest `ServiceContract` field** (§4): worth adding to enable server-side `Get<TService>()`, or
   YAGNI — wire purely by event and look kernels up by id?
5. **Does `UseKernel<TKernel>()` fully leave the public API** (§1), or stay as a deprecated forwarder to
   `Kernels.Register` for one release?
6. **Service interface as bare marker** (§2): is `interface IFooService : IEventKernel<TEvent> {}` with
   no members too thin to justify the concept, or is the naming/typing payoff enough?
