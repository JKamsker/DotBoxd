# Server-Side Projection Pipeline

**Status:** Implemented (P0–P5); see status note below
**Author:** (drafted with Claude Code)
**Related:** [`plugin-fluent-hooks-api`](../plugin-fluent-hooks-api/plan.md), [`remote-plugin-server-builder`](../remote-plugin-server-builder/plan.md), PR #63 (full marshaller type set + `record.get`)

> This doc captures the design for richer **server-side `Where`/`Select` projections** that feed a **client-side `RunLocal`**, so chains like the two below become first-class. It is grounded in the current analyzer/runtime — every claim cites a real symbol.

## Implementation status

| Phase | Delivered | Notes |
|---|---|---|
| **P0** record.get downstream-field reads | ✅ | collision-safe; filter-project-filter tests |
| **P1** `ctx` host calls in `Select` | ✅ | already reachable via `ctx.Host<T>()`; the invocation lowering ignores the receiver, so scalar host reads in `Select` projected to `RunLocal` work end-to-end |
| **P2** non-scalar host returns | ✅ | host bindings may return list/map/DTO/Guid/enum (verifier + runtime already accept these); analyzer gate relaxed to the marshaller manifest tag |
| **P3** member-chain reads (`.Count`, fields) | ✅ | recursive `LowerMemberAccess`; `list.count` over a projected list **and** a host-call result; record-field gating fixed to use the manifest tag (a `List` exposes `Count`/`Capacity` and was misread as a record) |
| **P4** anonymous-type projections | ✅ **intermediate + terminal** | anon tuples lower to `record.new` and filter server-side; a **terminal** anon projection is wired by a **generic interceptor** (Roslyn infers the type parameters at the call site, so the source never names the anon type) and decoded by the **reflective registration** via the anon type's public positional constructor — full round-trip, see §5.4 |
| **P5** derived-ctor-field handling | ✅ **fail-safe** | a field set only in the ctor body is rejected (chain skipped), never silently dropped; delivered as fail-safe-skip + test rather than a separate analyzer warning (the generator path skips silently and a new diagnostic would churn the diag/baseline catalog) |

The marquee `Where → Select(ctx.…GetInRange(id, 4).Count) → Where(count) → RunLocal` shape (P1+P2+P3) is covered end-to-end against the real binding-dispatch path.

`Guid` is a first-class sandbox scalar across every layer the pipeline touches — wire codec, sandbox/wire converter, reflective marshaller, **and** the kernel JSON literal export/import (canonical hyphenated form). It is intentionally **not** a valid map *key* (the verifier's key set is bool/int/long/string/opaque-id); a `Dictionary<Guid,V>` is rejected up front at marshal time, mirrored in both `SandboxTypeOf` and `ToSandboxValue`.

---

## 1. Motivation — the two target chains

A plugin author wants to write a server-side filter+project pipeline whose result is delivered to a local (in-plugin) callback:

```csharp
// (1) anonymous-type projection with ctx host calls
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)                              // server-side
    .Select((e, ctx) => new                                  // server-side, anonymous type
    {
        Monster = ctx.Monsters.GetId(e.Monster),
        players = ctx.Players.FindByRange(e.Monster.Position, 4)
    })
    .Where(x => x.players.Count > 3)                          // server-side
    .RunLocal((x, ctx) => { /* client-side */ });

// (2) named DTO with a logic-bearing constructor
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)
    .Select((e, ctx) => new MonsterAggroInfo(e.Monster, ctx.Players.FindByRange(e.Monster.Position, 4), e.Distance))
    .Where(info => info.Players.Count > 3)
    .RunLocal((info, ctx) => { /* client-side */ });
```

These are written against an **entity-graph** mental model. The platform is deliberately an **immutable-value, verified-IR, push-only** model. This doc defines how to honor the *intent* — server-side filter/project (including host calls), then a local callback over the result — within that model, and lists the analyzer work to get there.

### Locked product decisions (from review)

| Decision | Choice |
|---|---|
| Host entities crossing the wire | **Read-only snapshots** — project ids/scalars, not live handles. No reverse-RPC, no client-side mutation of server state. |
| Side effects (e.g. "set aggro to 0") | A **separate server-side call** keyed by id (the existing `server.Api.Monsters.Get(id).…` service idiom), invoked from `RunLocal`. **Not** in scope as a mutation of the projected snapshot. |
| Projection shape | Named DTOs anywhere; anonymous types both as **intermediate** server-side projections and as the **terminal** pushed value (wired by a generic interceptor + reflective decode — see §5.4). Named DTOs remain the default (fast generated decoder; nominal/shareable). |
| DTO constructor with derived fields | **Every persisted field is an explicit constructor argument.** The ctor body is **not** replicated into IR (see §5.4). |
| Aggregates (e.g. "players nearby") | The **worked examples use `GetInRange(...).Count`** (collection host-return + member-chain `.Count`) to **prove the capability** — a scalar `CountInRange` is then a trivial subset, and a real host API would add performant scalar aggregates where needed. Both forms supported. |
| Host-call lowering | **One dialect** — the hook-chain `Select`/`Where` path reuses the kernel's host-binding lowerer rather than forking. |

The original `x.Monster.AggroRange = 0` line is explicitly **out of scope** as a wire mutation — it was a slip; the real write happens through a server API call by id.

---

## 2. Execution model — the server/local boundary

This is the spine of the whole feature and must be stated unambiguously:

```
On<TEvent>()                         server: subscription
  .Where(pred)        ─┐
  .Select(proj)        ├─ SERVER-SIDE: lowered to verified kernel IR.
  .Where(pred)        ─┘  Runs in the sandbox. May read event fields,
  ...                      call capability-gated host services (ctx.*),
                           and filter. Only the FINAL projected value of a
                           surviving event is encoded to the wire.
  .RunLocal(cb)            CLIENT-SIDE (in-plugin native C#): receives the
                           decoded, immutable snapshot + a client HookContext.
```

Invariants:

- **Everything before `RunLocal` is server-side, verified IR.** Filtering happens on the server, so non-matching events never cross the wire (proven today by `RemoteRunLocalChainRuntimeTests`).
- **`RunLocal` is local and side-effect-light.** It gets a by-value snapshot. It can emit messages (`ctx.Messages.Send`) and call back into server services (request/response) keyed by ids. It **cannot** mutate the snapshot back into server state — there is no reverse channel, by design.
- **Only wire-eligible values cross.** `KernelRpcValueKind` = `Unit/Bool/I32/I64/F64/String/List/Record/Map/Guid` (`src/Hosting/DotBoxD.Plugins/Runtime/Rpc/KernelRpcValue.cs`). A live `Monster`/`Player` is **not** wire-eligible; its **id** (string/Guid) is.
- Contrast with `.Run(...)` (server-side terminal, e.g. `ctx.Messages.Send` in the verified sandbox) vs `.RunLocal(...)` (client-side terminal). This doc is about `RunLocal` chains.

---

## 3. Current platform state (grounded)

### 3.1 Events are flat scalar records

```csharp
// samples/GameServer/Examples.GameServer.Server.Abstractions/Events/MonsterAggroEvent.cs:9
public sealed record MonsterAggroEvent(
    string MonsterId, string PlayerId, int Distance, int MonsterLevel, int PlayerLevel);
```

There is no `Monster` object, no `Position`, no `Players` collection on the event. The snippets' `e.Monster` / `e.Monster.Position` do not exist — the projection must obtain that data via **host calls** keyed by `MonsterId`/`PlayerId`.

### 3.2 The host/world API surface (the snapshot idiom already exists)

```csharp
// samples/.../IGameWorldAccess.cs
[RpcService] public interface IGameWorldAccess { IMonsterControl Monsters { get; } IEntityControl Entities { get; } }

[RpcService] public interface IMonsterControl {
    [HostBinding("game.world.monster.read.handle")] IMonster Get(string entityId);
    [HostBinding("game.world.monster.read.kind")]   ValueTask<bool> IsMonsterAsync(string entityId);
}

[RpcService] public interface IMonster : IEntity {
    [HostBinding("game.world.monster.read.snapshot")] ValueTask<MonsterSnapshot> SnapshotAsync();
    [HostBinding("game.world.monster.write.kill")]    ValueTask<bool> KillAsync();
    [HostBinding("game.world.combat.threat")]         ValueTask<int>  GetThreatAsync();
    [HostBinding("game.world.monster.write.position")] ValueTask      TeleportToAsync(int position);
}
```

- A method is **callable from lowered IR** when it carries `[HostBinding]` *or* auto-binds (ordinary, non-static, non-generic method on a `[RpcService]` interface); binding id = `host.{ns}.{Type}.{Method}`, capability from `[HostBinding]`.
- **Effects** are inferred: `Cpu` always; `Alloc` for allocating returns; `HostStateWrite` if the method name starts `Kill|Set|Update|Delete|Add|Remove|Move|Teleport` or the capability contains `.write.`; else `HostStateRead`.
  (`src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs`)
- **There is no aggro setter today** — the closest writes are `KillAsync` / `TeleportToAsync`.

### 3.3 Host calls already lower inside `Select`/`Where`

```csharp
// tests/.../PluginAnalyzerHookChainProjectionTests.cs:36
hooks.On<ProbeEvent>()
    .Select((e, ctx) => ctx.Host<IProbeWorld>().Label(e.TargetId))  // host call IN a Select — works today
    .Where(label => label == "ready")
    .Run((label, ctx) => ctx.Messages.Send(label, label));
```

The invocation path (`DotBoxDInvocationExpressionLowerer` → `DotBoxDHostBindingExpressionLowerer.TryLower`) is reachable from `Select`/`Where` bodies, and capabilities/effects are collected into the chain manifest. **This is the foundation the feature builds on.**

### 3.4 The precise gaps

| # | Gap | Where it fails today | Effort |
|---|---|---|---|
| G1 | Bare `ctx` identifier / `ctx.Prop.Method()` chain in `Select`/`Where` | `ctx` name is parsed by `HookChainStageLowerer.LambdaParameters` (line 256) but **discarded** in `LowerSelect`/`Context`; a bare `ctx` throws "Unsupported plugin identifier" at `DotBoxDExpressionModelFactory.cs:263`. | medium |
| G2 | Host calls returning **non-scalars** (list of ids, DTO, opaque handle) | Return validation is scalar-only: `DotBoxDHostBindingExpressionLowerer.cs:33-38` throws "must return a supported scalar type" (even though `ReturnAllocates` already anticipates list/DTO/Guid). | medium |
| G3 | **Member chains** on projected values: `x.players.Count`, `x.Monster.Name` | `LowerMemberAccess` only handles single-hop `identifier.Member`; a nested `MemberAccess` falls to `Unsupported()` at `DotBoxDExpressionModelFactory.cs:300`. The `list.count` intrinsic already exists (`SandboxCollectionFuel`, `ListValue.Count`). | medium (collections) / large (handle props) |
| G4 | **Anonymous-type** projections `new { … }` | `DotBoxDExpressionModelFactory.Lower` has no `AnonymousObjectCreationExpressionSyntax` case → `Unsupported()`. Decoder emit must avoid *naming* the synthesized type. | medium |
| G5 | DTO **ctor body** deriving a field (`MonsterName = monster.Name`) | `DotBoxDRecordCreationExpressionLowerer` maps declared fields → ctor args positionally and emits `record.new` with only the passed args; the ctor body never runs server-side → derived field is absent from the IR record. | n/a (policy: pass as explicit arg) |
| G6 | A **server-side aggro write** surface | `IMonster` has no aggro setter. | small |
| ✓ | Downstream `Where`/`Select` reading a **projected record field** by name | **Done in PR #63**: `record.get` via `LowerProjectedRecordField` (`DotBoxDExpressionModelFactory.cs:308-337`) + `ProjectedElementType` threading; collision-safe. | landed |

---

## 4. Design overview

The feature is the union of five analyzer capabilities (G1–G4, G6) over the existing wire/marshalling layer. Nothing here adds a wire kind or a reverse channel — it stays inside the immutable-value, push-only model.

```
        ┌────────────────────────── server-side (verified IR) ──────────────────────────┐
event ─▶ Where(pred) ─▶ Select(proj w/ ctx.* host reads) ─▶ Where(proj-field pred) ─▶ encode ─┐
        └───────────────────────────────────────────────────────────────────────────────────┘ │
                                                                                          wire (Record/List/scalars)
        ┌──────────────── client-side (native) ───────────────┐                                 │
        │ decode snapshot ─▶ RunLocal(cb): read fields, send   │ ◀───────────────────────────────┘
        │ messages, call server API by id (separate request)   │
        └──────────────────────────────────────────────────────┘
```

---

## 5. Capability designs

### 5.1 Bind the `Select`/`Where` `ctx` parameter (G1)

**Goal:** make `(e, ctx) => …` bodies able to reference `ctx` and chain into host services, reusing the existing host-binding lowering.

**Change:**
1. Add `string? ContextParameterName` to `DotBoxDExpressionLoweringContext` (alongside `EventParameterName`, `ProjectedElementName`).
2. In `HookChainStageLowerer.LambdaParameters` keep both names; stop discarding the second in `LowerSelect`/`BuildShouldHandle`; thread it into the `Context(...)` builder.
3. In `DotBoxDExpressionModelFactory`:
   - `LowerMemberAccess` / `LowerInvocation`: when the left-most receiver identifier equals `ContextParameterName`, treat the node as a **host-service access**. Route the *invocation* through `DotBoxDHostBindingExpressionLowerer.TryLower` (semantic-model resolves `ctx.Monsters.Get(id)` to an `IMethodSymbol`; the receiver chain `ctx.Monsters` is a service selector, not a lowered value).
   - A bare `ctx` with no invocation (e.g. used as a value) stays unsupported — `ctx` is a *capability selector*, not a sandbox value.

**Result:** `ctx.Monsters.IsMonsterAsync(e.MonsterId)` (scalar return) lowers; capabilities/effects flow into the manifest exactly as `ctx.Host<T>().M()` does today.

> Note: this generalizes the existing `ctx.Host<T>()` form to the auto-bound `[RpcService]` property-chain form. Both resolve to a bindable `IMethodSymbol`; the only new work is letting the *receiver* be the bound `ctx` parameter rather than a `Host<T>()` call.

### 5.2 Non-scalar host-call returns (G2)

Relax `DotBoxDHostBindingExpressionLowerer`'s return check from "scalar-only" to "**wire-eligible**", reusing the marshaller-eligibility predicate already used for projections in PR #63 (`DotBoxDRpcTypeMapper.IsRecordDto` / list element / Guid / enum). This lets a host read return:

- `int`/`string`/… (scalars — today),
- `List<string>` (e.g. nearby player **ids**) → enables `.Count` downstream,
- a scalar DTO snapshot (e.g. `MonsterSnapshot` of scalars).

It must **not** accept a live entity (`Monster`/`Player`) — those remain unmarshallable and fail-safe. `ReturnAllocates` already adds the `Alloc` effect for these shapes, so effect inference is consistent.

### 5.3 Member-chain reads (G3)

Make `LowerMemberAccess` recursive over **any** inner expression — not just a `MemberAccessExpressionSyntax`. The inner `member.Expression` may be a projected field (`x.players`), **a host-call result** (`ctx.Players.GetInRange(…)`), or another member access. Lower it first (yielding IR **and** its sandbox type), then dispatch on that type:

- inner type is a **List** and member is `Count` → emit the existing `list.count` intrinsic (`new CallExpression("list.count", [inner], …)`);
- inner type is a **record** and member is a field → `record.get` (extends the single-hop `LowerProjectedRecordField` from PR #63);
- inner type is an **opaque handle** and member is a property → **not supported in v1** (would need a host read per access). Authors pre-project the scalar instead (e.g. project `MonsterName` rather than read `x.Monster.Name`).

The key generalization: `ctx.Players.GetInRange(e.MonsterId, 4).Count` has `member.Expression` = an **invocation** (the host call), so the recursion must accept invocations as the inner node, lower them via the host-binding path (§5.1/§5.2), then apply `list.count`. The same `list.count` serves both a host-call result and a projected field.

This requires threading the *current sandbox type* down the chain (the context already carries `ProjectedElementType` for the top hop; intermediate hops need the same). Effort: medium for collection/record chains; the handle-property case is deliberately deferred.

### 5.4 Projections: named DTOs and anonymous types (G4, G5)

**Named DTOs** already work (`record.new` + `record.get` + generated `ReadProjected` that reconstructs via the positional ctor — `RpcKernelValueConversionEmitter.Dto.cs` `TryResolveConstructor`/`BuildDtoReconstruction`). The one rule authors must follow (G5):

> **Every field carried over the wire must be an explicit constructor argument.** The constructor *body* does not run server-side; `Select(e => new MonsterAggroInfo(…, monsterName, …))` must pass `monsterName` in, not derive it inside the ctor. A field set only in the ctor body is absent from the IR record and unreadable by any downstream server-side `record.get`. (An analyzer **diagnostic** should flag a DTO field that is neither a ctor parameter nor otherwise populated — fail loud, not silent.)

**Anonymous types** (`new { A = …, B = … }`) — implemented by `DotBoxDAnonymousObjectCreationExpressionLowerer`, **as intermediate server-side projections only**:

1. Resolve the synthesized type via `model.GetTypeInfo(node)`; it is an `INamedTypeSymbol` with `IsAnonymousType` whose `TypeKind` is `Class`, so `IsRecordDto` already treats it as a structural record (public props, declaration order).
2. Lower each initializer in declaration order → emit `record.new` (identical to the named path). A downstream `Where`/`Select` reads `x.A` via `record.get` by member name (§5.3). So an anonymous tuple can be built server-side, filtered on its fields, and transformed — e.g. `Select(e => new { Id = e.Id, N = … }).Where(x => x.N > 3).Select(x => x.Id)`.

**Terminal anonymous projections — supported via a generic interceptor.** An earlier iteration believed a terminal anonymous projection was infeasible (the interceptor's `Action<TProjected, HookContext>` handler parameter can't *name* an anonymous type in source). The resolution: an anonymous type has a real **metadata identity** — it's a legal type *argument* even though it has no source-nameable name — so the interceptor is emitted as a **generic method** and Roslyn infers the type arguments (including the anonymous one) at the intercepted call site:

```csharp
[InterceptsLocation(...)]
public static RemoteHookPipeline<TEvent> Intercept_0<TEvent, TCurrent>(
    this RemoteHookStage<TEvent, TCurrent> pipeline,        // TCurrent binds to the anon type
    System.Action<TCurrent, HookContext> handler)
    => pipeline.UseGeneratedLocalChain(Package.Create(), handler);   // 2-arg reflective form, no decoder
```

Key points:
- The interceptor's generic **arity must match** the interceptable method's context (CS9177): `RunLocal` lives on `RemoteHookStage<TEvent, TCurrent>`, so **both** type arguments become parameters (`HookChainModelFactory.RewriteWithTypeParameters` abstracts every receiver type argument; the emitter writes the `<TEvent, TCurrent>` list).
- **No `ReadProjected` decoder** is emitted for the anon case (a static method cannot declare an un-nameable return type) — `HasLocalDecoder` is false and the chain uses the **2-arg reflective registration** (`RemoteLocalHandlerRegistry.Register<TProjected>`), which reconstructs the anonymous type unchanged: anon types are DTO-shaped (public positional constructor + declaration-order properties), so `KernelRpcMarshaller.FromSandboxValue` builds them like any record.
- **Fody is not involved.** C# interceptor call-site redirection is resolved by Roslyn *during* compilation, so a Cecil-injected interceptor (added post-build) would never be wired — even though Cecil *can* name an anon type by metadata token. The generic interceptor is the correct vector; reflection is the decode path.

**Author guidance:** a named record/DTO is still the default for the pushed value (nominal, shareable, and it keeps the fast generated decoder). An anonymous terminal works but uses the slower reflective decode and is structural-only — usable inside the `RunLocal` lambda, never passed onward.

### 5.5 Read-only snapshots + side effects by id (locked model, G6)

Entities are referenced by **id**. To make the snippets' real intent (set aggro to 0) work, add a small write surface:

```csharp
[HostBinding("game.world.monster.write.aggro")]
ValueTask SetAggroRangeAsync(int range);   // on IMonster, sibling of TeleportToAsync
```

`RunLocal` performs the write as a **separate server call** keyed by the projected id (request/response service), e.g. `await server.Api.Monsters.Get(info.MonsterId).SetAggroRangeAsync(0);` — *not* by mutating the snapshot. This keeps the server the sole mutation authority and the wire push-only.

### 5.6 Host API the worked examples assume

The examples in §6 call `ctx.Players.GetInRange(...)`, which does not exist yet. The current `IGameWorldAccess` exposes `Monsters` (`IMonsterControl`) and `Entities` (`IEntityControl`) but no player control. The examples assume two additions:

```csharp
[RpcService]
public interface IPlayerControl
{
    // Returns player IDs (scalars), NOT live Player objects — a live entity is not marshallable
    // across the host-call boundary, even when only .Count is read.
    [HostBinding("game.world.player.read.in_range")]
    IReadOnlyList<string> GetInRange(string monsterId, int radius);
}

// on IGameWorldAccess:
IPlayerControl Players { get; }

// a scalar name read used in §6 (sibling reads on the monster control):
[HostBinding("game.world.monster.read.name")]
string GetName(string monsterId);   // on IMonsterControl
```

**Why `GetInRange(...).Count` and not a bespoke `CountInRange`** — deliberate, for *proof of capability*. The examples use the more demanding form (a collection host-return + a member-chain `.Count`, exercising G2 **and** G3) precisely to show the pipeline is general enough that a scalar aggregate (`int CountInRange(...)`, needing only G1) is a trivial subset. Performance is not the point of the example: a production host API would expose performant scalar aggregates where they matter. The design must not *depend* on the host pre-aggregating — so the example proves it doesn't.

When the result of `GetInRange(...).Count` is a scalar field (as in §6), the id list is a **server-side temporary**: marshalled into the sandbox, counted via `list.count`, and discarded. Only the resulting `int` crosses the plugin wire. Projecting the list itself (`PlayerIds = ctx.Players.GetInRange(...)`) is the variant that puts the list on the wire and makes the ids available in `RunLocal`.

---

## 6. Worked rewrites (compile on the target design)

### 6.1 Anonymous-type chain (snippet 1, retargeted)

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)                                       // server IR
    .Select((e, ctx) => new                                           // server IR; anonymous record
    {
        MonsterId     = e.MonsterId,                                  // scalar passthrough
        MonsterName   = ctx.Monsters.GetName(e.MonsterId),            // host read -> string      (G1)
        NearbyPlayers = ctx.Players.GetInRange(e.MonsterId, 4).Count, // host read List + .Count   (G1+G2+G3)
    })
    .Where(x => x.NearbyPlayers > 3)                                  // server IR; reads projected scalar
    .RunLocal((x, ctx) =>                                             // client-side
    {
        ctx.Messages.Send(x.MonsterId, $"Calming {x.MonsterName}: {x.NearbyPlayers} nearby");
        // side effect by id, server-side write (out of the lowered chain):
        // await server.Api.Monsters.Get(x.MonsterId).SetAggroRangeAsync(0);
    });
```

> `NearbyPlayers = ctx.Players.GetInRange(...).Count` deliberately exercises the full collection path (G2 host-list return + G3 member-chain `.Count`) to prove the capability — the id list is counted server-side and discarded; only the `int` crosses the wire (see §5.6). A real host API could expose a scalar `CountInRange` (G1 only) for performance, and `RunLocal` could instead receive the ids by projecting the list directly (`PlayerIds = ctx.Players.GetInRange(e.MonsterId, 4)` then `x.PlayerIds.Count`).

### 6.2 Named-DTO chain (snippet 2, retargeted)

```csharp
public sealed record MonsterAggroInfo(            // all scalars; every field a ctor parameter
    string MonsterId,
    string MonsterName,                           // computed in the Select, passed in (not derived in ctor)
    int    Distance,
    int    NearbyPlayers);

server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 4)
    .Select((e, ctx) => new MonsterAggroInfo(
        e.MonsterId,
        ctx.Monsters.GetName(e.MonsterId),                       // host read -> string     (G1)
        e.Distance,
        ctx.Players.GetInRange(e.MonsterId, 4).Count))           // host read List + .Count  (G1+G2+G3)
    .Where(info => info.NearbyPlayers > 3)                       // server IR; record.get (landed)
    .RunLocal((info, ctx) =>
    {
        ctx.Messages.Send(info.MonsterId, $"{info.MonsterName} calmed ({info.NearbyPlayers} nearby)");
        // await server.Api.Monsters.Get(info.MonsterId).SetAggroRangeAsync(0);
    });
```

Both worked chains exercise the **collection aggregate** (`GetInRange(...).Count`) on purpose, so the examples prove G1 + G2 + G3 together. **§6.2 needs G1 + G2 + G3 + G6**; **§6.1 adds G4** (anonymous type). The smallest end-to-end *sub-slice* a single phase can prove is a scalar `ctx` read (e.g. `ctx.Monsters.GetName(...)`, G1 only) — useful as a P1 milestone — but the worked examples as written complete only once P1–P3 (and the host API in §5.6) are in.

---

## 7. Phased implementation plan

| Phase | Scope | Gaps | Risk |
|---|---|---|---|
| **P0** | Land the PR #63 `record.get` + filter-project-filter work (already in branch) as the downstream-field-read foundation. | ✓ | none |
| **P1** | **Bind `ctx` in `Select`/`Where`**, **reusing the kernel's host-binding lowerer** (one dialect, per decision). Scalar host reads only. Add the `GetName` scalar read + the `SetAggroRangeAsync` write surface (G6) and the `IPlayerControl`/`Players` plumbing (§5.6). Tests: a `ctx`-scalar-read projection (`GetName`) round-trips; capability/effect manifest is correct. | G1, G6 | low |
| **P2** | **Non-scalar host returns** (G2): allow wire-eligible list/DTO returns from host reads (e.g. `GetInRange` → `List<string>`), reusing the marshaller-eligibility predicate. Tests: a projected `List<string>` round-trips; a live-entity return still fails safe. | G2 | low–med |
| **P3** | **Member-chain lowering** (G3): recursive `LowerMemberAccess` over any inner node — projected field **and host-call result**; `list.count` + record-field chains; thread intermediate sandbox type. Tests: `ctx.Players.GetInRange(...).Count > 3` and `x.PlayerIds.Count > 3` both filter server-side; handle-property chain reports a clear diagnostic. **Completes the §6.2 named-DTO chain end-to-end.** | G3 | med |
| **P4** | **Anonymous-type projections** (G4): new lowerer + `new { }` reconstruction in the decoder; author-facing note on structural-only identity. **Completes the §6.1 anonymous chain.** Tests: anonymous projection round-trips over both decode paths; member name/type/order mismatch is rejected. | G4 | med |
| **P5** | **Diagnostics & docs** (G5): analyzer warning for a DTO field that is not a ctor parameter (derived-in-ctor field would silently vanish); author guide + samples; spec/manifest updates. | G5 | low |

Each phase is independently shippable and leaves the chain working for the shapes it covers; later phases are pure additions. The two **worked examples** (§6) light up incrementally: P1 proves the `ctx`-read mechanism on a scalar, P2+P3 complete §6.2, and P4 completes §6.1.

---

## 8. Test plan

Mirror the existing `RemoteRunLocalChainRuntimeTests` matrix (server-side filter → encode → wire → **both** decode paths: reflective fallback + generated `ReadProjected`):

- **P1:** `Select` with a `ctx` scalar host read round-trips; non-matching event filtered before any push; manifest carries the host capability + `HostStateRead` effect. Negative: bare `ctx` value → clear `NotSupportedException`/diagnostic.
- **P2:** host read returning `List<string>` projects and round-trips; returning a non-wire-eligible entity type fails at lowering with a precise message.
- **P3:** both `ctx.Players.GetInRange(...).Count > 3` (`.Count` on a **host-call result** — list counted server-side, never on the wire) and `x.PlayerIds.Count > 3` (`.Count` on a **projected list**) discriminate server-side; a collision-style fixture proves the count is read from the list, not a same-named event field; handle-property chain → diagnostic.
- **P4:** anonymous `new { A, B, C }` projection round-trips field-for-field over both decode paths; reordered/renamed reconstruction fails to compile (guards the structural-unification contract).
- **P5:** a DTO whose ctor derives a field triggers the analyzer warning; the worked §6 chains compile and run end-to-end as sample integration tests.

API-baseline + spec-manifest updates per the usual gates (`docs/api-baselines`, `check-package-metadata.ps1`).

---

## 9. Non-goals / risks / open questions

**Non-goals**
- Live, mutable host handles on the wire; reverse-RPC write-back; client-driven server mutation. (Erodes the verified-IR / immutable-wire guarantee; the id + server-call idiom covers the use cases.)
- Replicating arbitrary DTO constructor logic into IR.
- Opaque-handle **property** reads inside a chain (`x.Monster.Name`); use a projected scalar.

**Risks**
- *Anonymous-type structural contract* (P4): the generated `new { }` must match the projection's shape exactly. Mitigation: derive the reconstruction directly from the same `INamedTypeSymbol` member list used for `record.new`, so the two are generated from one source of truth; add a compile-time guard test.
- *Host-call lowering convergence*: the kernel path already lowers `world.Monsters.Get(id).KillAsync()` via opaque handle values, while the hook-chain `Select` path is more restricted. **Decision: P1 reuses/unifies the kernel's host-binding lowerer** rather than forking a second dialect.
- *`ctx` ambiguity*: `ctx` is the host-service selector in `Select`/`Where` (server) but the client `HookContext` in `RunLocal`. They are different objects in different execution domains — document clearly and keep the analyzer’s `ctx` handling scoped to lowered stages only.

**Resolved**
- The server-side side-effect call (`SetAggroRangeAsync` by id) stays a **plain service call** — no fluent sugar.
- **Unify** the kernel and hook-chain host-call lowerers in P1 (reuse the kernel's host-binding lowering; do not fork a second dialect).
- The **worked examples use `GetInRange(...).Count`** (collection host-return + member-chain `.Count`) to prove the pipeline is general; a scalar `CountInRange` (G1 only) is a trivial subset that a real host API would add for performance. Both forms are supported.

No open questions remain; the plan is ready to execute.
