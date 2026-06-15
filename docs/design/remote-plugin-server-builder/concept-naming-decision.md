# Plugin facade concept naming — Decision record

Companion to [plan.md](plan.md), [invoke-async.md](invoke-async.md), and
[../plugin-fluent-hooks-api/kernel-binding-model.md](../plugin-fluent-hooks-api/kernel-binding-model.md).

**Status:** Decided (locked) — all micro-decisions resolved (§9). **Date:** 2026-06-15 (§9 resolved 2026-06-16).

This record renames the plugin-facing facade surfaces (`server.X`). It is a **naming + concept**
decision: the underlying mechanisms (verified-IR install, capability gating, session ownership, the
generated typed client) are unchanged. Existing design docs that predate this record still use the old
surface names (`server.Kernels`, `server.KernelRpc`, `server.Hooks.InvokeKernel`, `server.Events`); those
names are superseded here, not their machinery.

---

## 1. Problem

The word **kernel** names the substrate (the authored unit lowered to verified IR) *and* almost every
facade surface (`server.Kernels`, `server.KernelRpc`, `Hooks.InvokeKernel`, `Kernels.InvokeAsync`,
`[KernelRpc*]`). The API reads "kernel here, kernel there." We want **role-based** surface names so the
substrate word recedes, plus a home for a **new fifth concept** (a single-occurrence handler that returns
a value to the server).

## 2. The deep structure (why the names fall out cleanly)

Every surface is a point on a few axes. Naming follows the axes.

| Axis | Values |
|---|---|
| **Who initiates** | server raises (#1, #3, #4, #5) vs plugin calls (#2, ad-hoc) |
| **Who consumes the return value** | nobody/effects (#1, #3, #4) · the **plugin** (#2, ad-hoc) · the **server** (#5) |
| **Author form** | named class (#1, #2, #5) vs inline lambda (#3, #4, ad-hoc) |
| **Scope** | a whole published service surface (#1) vs a single occurrence (#5) |

Two consequences drive the whole scheme:

- **#2 and #5 are mirror images.** Both are request/response that return a typed value. #2 = *plugin*
  asks, *plugin* consumes. #5 = *server* asks, *server* consumes.
- **Anonymous-inline vs named-class is one recurring choice**, not five surfaces (see §4).

## 3. Decisions (locked)

> **Rule that drives everything (Decided):** `kernel` names **only** the authored unit of verified IR —
> the substrate. It survives on internals (`KernelRegistry`, `InstalledKernel`, `KernelPackageRegistry`,
> `IEventKernel<TEvent>`, `[Plugin]`, `[KernelMethod]`) and appears on **no** `server.X` facade surface.

| # | Concept | Locked surface / verb | Type / attribute | Replaces |
|---|---|---|---|---|
| — | substrate (verified IR) | *(none — internal only)* | `KernelRegistry`, `InstalledKernel`, `IEventKernel<TEvent>`, `[Plugin]` | *(kept)* |
| 1 | replace a whole published service | `server.Services.Replace<TContract, TKernel>()` | contract `IMonsterAggroService`; class `GuardianKernel`; `[Plugin]` | `server.Kernels.Register` |
| 2 | extend an IPC surface with a server-side method | `server.World.<Domain>.Extend<TContract, TKernel>()` | `[ServerExtension]` (was `[KernelRpcService]`); client attrs → `[ServerExtensionClient]` / `[ServerExtensionMethod]` | `server.KernelRpc.Register` |
| 3 | inline event reaction (effect) | `server.Hooks.On<TEvent>().Where(..).Run(λ)` / `.RunLocal(λ)` | *(inline; no class)* | terminals `InvokeKernel` → `Run`, `InvokeLocal` → `RunLocal` |
| 4 | inline event notify (fire-and-forget) | `server.Notifications.On<TEvent>()...RunLocal(λ)` | facade `RemoteNotificationControl` | `server.Events` / "Subscriptions" |
| 5 | **NEW** single-occurrence decision (returns to server) | `server.Hooks.On<TEvent>().Where(..).Use<TKernel>()` | `IDecisionHandler<TEvent,TResult>`; class `FireWardDecision`; `[Decision]` | *(new)* |
| — | ad-hoc anonymous server-side call | `server.InvokeAsync(λ)` *(top-level)* | *(inline; capture-bag overload kept)* | `server.Kernels.InvokeAsync` |

Notes on the picks:

- **`Services.Replace`** — reads as DI substitution; the server already ships a default, so the plugin
  *replaces* it. **Decided** (over `Provide`/`Register`): a registration **overrides** the default with
  last-registration-wins semantics — one effective impl per contract. Per-contract fan-out (e.g. per-user,
  the `GetAll<TService>` case in [kernel-binding-model.md §5](../plugin-fluent-hooks-api/kernel-binding-model.md))
  would be a separate **additive** surface, never `Replace`.
- **#2 is not a standalone registry.** Its invocation already lives on the IPC/World surface
  (`server.World.Monsters.KillMonstersAsync(...)`, grafted by `[KernelRpcClientProperty(typeof(RemoteMonsterControl))]`).
  We **co-locate registration there** via `.Extend<…>()` and **delete** the floating `KernelRpc`/`ServerCalls`
  surface. "RPC" is retired — the feature *eliminates* round-trips. (Details: §6.)
- **`Hooks` stays** and becomes the single event-reaction entrypoint (inline *and* class). Terminal trio:
  `.Run(λ)` (lowered effect) · `.RunLocal(λ)` (native escape) · `.Use<TKernel>()` (attach a named class).
- **`Notifications`** replaces `Events`/`Subscriptions` to dodge two live collisions: server-side
  `PluginServer.Events` (adapter registry) and the IR-manifest `Subscriptions` field.
- **`server.InvokeAsync`** moves to the top level — the anonymous call has no name to hang on a registry,
  so one token deep is correct. (`RemotePluginServer.RunAsync` already exists for lifecycle; `InvokeAsync`
  is free.)

## 4. The organizing principle: anonymous-inline vs named-class

The five surfaces collapse onto **two trigger directions × {anonymous, named}**:

| Trigger | Anonymous (inline lambda) | Named (class) |
|---|---|---|
| **plugin → server**, returns to plugin | `server.InvokeAsync(λ)` | `server.World.<Domain>.Extend<…>()` (#2) |
| **server → reaction** | `Hooks.On<E>().Run(λ)` (#3) | `Hooks.On<E>().Use<TKernel>()` (#1 effect / #5 decision) |

`.Use<T>()` is the named twin of `.Run(λ)`. The class's interface decides what it is: a void-`Handle`
class is an effect; an `IDecisionHandler<TEvent,TResult>` class is a decision whose value flows to the
server. This is why **#5 needs no surface of its own** — it is a `.Use<>()` of a value-returning class.

## 5. Concept #5 — `Decisions`, full design

A **Decision** is a named kernel class the server asks to resolve **one occurrence**; the server folds the
returned typed value into its own logic (final damage / died-or-not). The plugin authors the function but
**never receives** the return value.

### Authoring

```csharp
// framework interface — sits beside IEventKernel<TEvent> in the shared abstractions
public interface IDecisionHandler<TEvent, TResult>
{
    bool    CanHandle(TEvent e, HookContext ctx);   // gate — structural twin of IEventKernel.ShouldHandle
    TResult Handle(TEvent e, HookContext ctx);      // value-returning — vs IEventKernel's void Handle
}

public readonly record struct DamageResolution(int FinalDamage, bool Lethal);

[Decision("fire-ward")]
public sealed partial class FireWardDecision : IDecisionHandler<DamageEvent, DamageResolution>
{
    [LiveSetting][Range(0, 100)] public int WardPercent { get; set; } = 40;

    public bool CanHandle(DamageEvent e, HookContext ctx) => e.DamageType == "fire";

    public DamageResolution Handle(DamageEvent e, HookContext ctx)
    {
        var reduced = e.RawDamage - (e.RawDamage * WardPercent / 100);
        var hp = ctx.Host<IGameWorldAccess>().GetHealth(e.TargetId);
        return new DamageResolution(reduced, hp <= reduced);
    }
}
```

> **Decided:** No per-decision marker interface (no `IDamageDecision`). The event is fixed by `On<TEvent>()`
> and `TResult` comes from the class, so the server finds the handler by `(eventName, resultType)`. Unlike
> #1 (where the server *references* `IMonsterAggroService`), #5 is contract-light.

### Registration — through the Hooks chain (no separate surface)

```csharp
server.Hooks.On<DamageEvent>()
    .Where((e, ctx) => e.DamageType == "fire")   // optional inline gate, AND-composed with CanHandle
    .Use<FireWardDecision>();
```

`.Where(..)` composes with the class's `CanHandle` exactly as #1's `Register().Where()` composes with
`ShouldHandle`. `.Where` is optional sugar once the class has `CanHandle`.

### Server consumes the value out-of-band

The hook chain *wires* the decision; the value goes to the **server**, which pulls it at its own call site:

```csharp
// SERVER simulation (trusted native code, e.g. GameWorld.ApplyDamage)
var resolution = await server.Decisions.Decide<DamageEvent, DamageResolution>(
    new DamageEvent(targetId, raw, "fire"),
    defaultResolution: new DamageResolution(raw, lethal: hp <= raw),   // the server's own default
    ct);

if (resolution.Lethal) world.Kill(targetId);
else                   world.SetHealth(targetId, hp - resolution.FinalDamage);
```

> **Decided — dispatch (MVP): ordered first-match-wins + server default.** Among installed decisions whose
> manifest `(eventName, resultType)` matches and whose `CanHandle` returns true, the **first** in order runs
> and its value is the answer. Order source: registration order → optional `[Decision(Priority=)]` →
> plugin-id tiebreak. **No match → the server's `defaultResolution`** passed at the call site. This mirrors
> #1's override model, is safe across mutually-distrusting plugins (one plugin's value is never silently
> blended with another's), and degrades gracefully on disconnect (decisions vanish → server default).
>
> An **explicit, server-authored** `Decide(..).Fold(seed, combiner)` for additive stacking is **deferred
> post-MVP** — cross-plugin composition stays in trusted server code, never in plugin IR.

### Why it is cheap to build

`CanHandle` is the structural twin of `IEventKernel.ShouldHandle` (`bool (TEvent, HookContext)`), so the
gate lowers with **no analyzer change** (same `AllInterfaces` walk as #1). The only structural delta is
`Handle` returning `TResult` instead of `void` — and that value-returning body + codec
(`KernelRpcValueConverter`, `InstalledKernel.InvokeRpcAsync`) **already exists** for #2. So #5 = #1's gate
lowering + #2's value path, a new *combination*, not new machinery. New, additive pieces only:
`IDecisionHandler<,>`, `[Decision]`, a result-preserving `DecisionRegistry`, an install verb (may reuse
#2's value-returning install path), a server-side `DecideAsync`, and a `DecisionEntrypoint` manifest kind.

## 6. The #2 ↔ #5 mirror, and where #2 lives in the IPC surface

The IPC surface has three layers; #2's **invocation already lives in it** — only its registration floated.

1. **Raw control plane** — `IGamePluginControlService` (`[DotBoxDService]` proxy over the named pipe).
   #2 wire verbs: `InstallKernelRpcAsync(json)` + `InvokeKernelRpcAsync(pluginId, bytes)` (from
   `IKernelRpcWireClient`). Also `InstallPluginAsync` (#1), `UpdateSettingsAsync`, lifecycle, direct domain
   ops (`KillMonsterAsync`, `GetEntity*`).
2. **Typed domain client** — `RemoteWorldControl` → `server.World.Monsters` / `.Entities`. The analyzer
   grafts the generated `KillMonstersAsync` onto `RemoteMonsterControl` here, via
   `[KernelRpcClientProperty(typeof(RemoteMonsterControl))]` + `[KernelRpcClientMethod(...)]`.
3. **Floating registration** — `server.KernelRpc.Register<…>()` — the lone orphan, now removed.

> **Decided:** #2 *is* "extend an existing IPC/World surface with a server-side method." Register it on the
> surface it augments — `server.World.<Domain>.Extend<TContract, TKernel>()` — where the generated invoke
> method already lands. The standalone `KernelRpc`/`ServerCalls` surface is deleted, which also retires the
> weak `ServerCalls` name: there is no surface left to name, only the verb `.Extend<…>()`, and the concept
> is **"server extension."**

| | #2 `Extend` (`ServerExtension`) | #5 `Use<Decision>` (`Decision`) |
|---|---|---|
| Initiator | **plugin** calls | **server** raises one occurrence |
| Consumer of value | the **plugin** | the **server** |
| Gate | none (plugin-supplied args) | `CanHandle` (+ optional `.Where`) |
| Wire | `InvokeServerExtensionAsync` (plugin invokes) | `DecideAsync` (server invokes); **no** plugin invoke verb |

Same request/response coin, opposite faces — exactly the intended mental model.

## 7. Final shape — `Program.cs` read aloud

> **Decided (user):** `Build()` is **synchronous and does no I/O** — it only assembles the object graph
> (connection factory + queued setup callbacks). All async work — connect, ship verified IR, register — is
> deferred to `StartAsync()` (the `var app = builder.Build(); await app.RunAsync();` shape already locked in
> [plan.md](plan.md)). Samples use a plain **`using var server = …Build();`** (not `await using`); the
> facade exposes a synchronous `Dispose()` that tears down the connection `StartAsync()` opened.

```csharp
using var server = RemotePluginServerBuilder          // Build() is synchronous — pure construction, no I/O
    .FromPipeName(pipeName)
    .SetupServices(s => s                                                    // #1 replace whole services
        .Replace<IMonsterAggroService, GuardianKernel>()
        .Replace<IAttackService, RetaliationKernel>())
    .SetupWorld(w => w.Monsters                                             // #2 extend the IPC surface
        .Extend<IMonsterKillerService, MonsterKillerKernel>())
    .SetupHooks(h => h                                                      // #5 decision (returns to server)
        .On<DamageEvent>().Where((e, ctx) => e.DamageType == "fire").Use<FireWardDecision>())
    .Build();

await server.StartAsync();   // ALL async I/O happens here: connect → ship verified IR → register

// tune a replaced service's live settings (substrate handle unchanged)
await server.Services.Get<GuardianKernel>()
    .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true);

// ad-hoc anonymous server-side call — top level
var hp = await server.InvokeAsync((IGameWorldAccess w) => w.GetMonster("monster-2").Health);

// #2 invoked where it was registered — same World surface, one IPC call
var killed = await server.World.Monsters.KillMonstersAsync(["monster-3", "monster-4"]);

// #3 inline effect hook
server.Hooks.On<AttackEvent>()
    .Where((e, ctx) => e.AttackerLevel >= 5)
    .Run((e, ctx) => ctx.Messages.Send(e.AttackerId, "taunt"));

// #4 notify (fire-and-forget)
server.Notifications.On<AttackEvent>()
    .RunLocal((e, ctx) => { Telemetry.Count("attack"); return ValueTask.CompletedTask; });

await server.HoldUntilShutdownAsync();
```

Surface count drops from five (`Kernels` / `KernelRpc` / `Hooks` / `Events` / *new*) to **three**
(`Services`, `World.*…Extend`, `Hooks.On<>`) plus `Notifications` and two top-level verbs
(`InvokeAsync`, `HoldUntilShutdownAsync`). "kernel" appears on none of them.

## 8. Migration, collisions, risks

**Collisions to respect (verified against the code):**

- `PluginServer.Events` (live `PluginEventAdapterRegistry`) **and** the IR-manifest `Subscriptions` field
  are taken → #4 cannot keep `server.Events` cleanly; hence `Notifications`. `Decisions`/`Decide` were free
  at every layer.
- `KernelRegistry`, `InstalledKernel`, `IEventKernel<TEvent>` are deeply load-bearing across server +
  analyzer + manifest → **keep** them; they are what the renamed surfaces lower *to*.

**Cost tiers:**

- **Cheap / local to the facade shim + samples + docs:** `Kernels`→`Services`, terminals
  `InvokeKernel`→`Run` / `InvokeLocal`→`RunLocal`, `Events`→`Notifications`, move `InvokeAsync` to top
  level, add `Hooks.On<>().Use<>()`.
- **Lifecycle (small facade change):** `Build()` stays synchronous and I/O-free; the facade exposes a
  synchronous `Dispose()` so samples read `using var server = …Build();` — a move from today's
  `IAsyncDisposable`-only shim (`RemotePluginServer`). All connect/register I/O lives in `StartAsync()`.
- **Moderate:** `[KernelRpc*]` → `[ServerExtension*]` attribute family + the analyzer's generated-client
  emitter (it reads these attributes), and moving #2 registration to `World.<Domain>.Extend<…>()`.
- **High (cross-process IPC contract):** renaming wire verbs `InstallKernelRpcAsync` /
  `InvokeKernelRpcAsync` and `IKernelRpcWireClient`. **Recommendation:** sequence these last or behind
  `[Obsolete]` forwarders; the IR contract underneath is unchanged.
- **#5 is additive, not a rename** — lowest risk to existing code; reuses #1's gate lowering and #2's value
  codec.

**Accepted residual:** author classes keep mixed suffixes (`GuardianKernel`, `MonsterKillerKernel`,
`FireWardDecision`). The role now lives in the attribute (`[Plugin]` / `[ServerExtension]` / `[Decision]`),
not the class name; renaming existing classes to role suffixes is churn for marginal gain — deferred.

## 9. Micro-decisions (resolved)

1. **Event-reaction entrypoint placement** — **Decided: keep `server.Hooks.On<TEvent>()`.** The single
   event-reaction entrypoint (inline *and* `.Use<>` class) stays under `Hooks`; not promoted to top-level
   `server.On<>`. (`server.InvokeAsync` stays top-level regardless — the two need not be symmetric.)
2. **Decision terminal name** — **Decided: `.Use<TKernel>()`.** The general "promote this chain to a named
   class" terminal; it covers both effect classes and value-returning decisions. `.Decide<T>()` rejected as
   redundant.
3. **`Services` verb** — **Decided: `.Replace<TContract, TKernel>()`** (over `Provide`/`Register`). Override
   semantics, last-registration-wins; see §3 for the per-contract-fan-out caveat.

`CanHandle` / `Handle` method names are **locked** (the owner's MediatR framing from the outset); the
analyzer treats `CanHandle` as a second recognized gate name alongside `ShouldHandle`.

## 10. Provenance

Converged over a session on 2026-06-15: five independent naming lenses (DDD, .NET/MediatR, minimal-diff,
first-principles axes, call-site ergonomics) + a dedicated #5 design, synthesized, then refined by three
owner corrections — (a) #5 returns its value **to the server**, not the plugin; (b) #5 is like a Kernel
**service but scoped to one occurrence**; (c) fold #5's registration into the **Hooks** chain, drop the
`ServerCalls` surface into the **IPC/World** surface, and lift `InvokeAsync` to the **top level**; (d)
keep `Build()` **synchronous and I/O-free**, deferring all async work to `StartAsync()`.
