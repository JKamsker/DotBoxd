# Index-predicate metadata for lowered subscriptions

> Implements [issue #47](https://github.com/JKamsker/DotBoxD/issues/47):
> *Expose host-readable index metadata for lowered subscription predicates*,
> plus the follow-ups [#49](https://github.com/JKamsker/DotBoxD/issues/49) (live GameServer dispatch
> through the index), [#50](https://github.com/JKamsker/DotBoxD/issues/50) (a first-class framework index),
> and [#51](https://github.com/JKamsker/DotBoxD/issues/51), candidate 1 (kernel-class `ShouldHandle`
> extraction).

## Why

A host often has event-specific dispatch tables or indexes. Without predicate metadata the
only safe implementation is **broad subscription + run the lowered predicate for every event** —
correct, but expensive for high-volume event families.

DotBoxD already owns predicate lowering (`.Where(...).Select(...).Run(...)` → verified IR). This
feature makes DotBoxD *also* publish a structured, stable description of the index-eligible
constraints it found, so a host can compile that into whatever equality/range dispatch structure
is natural for its runtime — without inventing an expression-tree parser or leaking host-specific
filter DTOs into plugin code.

## What ships on the manifest

`HookSubscriptionManifest` carries two additive, back-compatible members
(`src/Hosting/DotBoxD.Plugins/PluginManifest.cs`):

```csharp
public sealed record HookSubscriptionManifest(string Event, string Kernel)
{
    public IReadOnlyList<IndexedPredicate> IndexedPredicates { get; init; } = [];
    public bool IndexCoversPredicate { get; init; }
}

public sealed record IndexedPredicate(
    string Path, IndexPredicateOperator Operator, object? Value, string ValueType);

public enum IndexPredicateOperator
{
    Equals, NotEquals, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
}
```

For the chain

```csharp
server.Subscriptions.On<AttackEvent>()
    .Where(e => e.AttackerId == "player-1" && e.Damage >= 5)
    .Select(e => e.TargetId)
    .Run((targetId, ctx) => ctx.Messages.Send(targetId, "watched-hit"));
```

the generated manifest subscription serializes to:

```json
{
  "event": "DotBoxD.Kernels.Game.Server.Abstractions.Events.AttackEvent",
  "kernel": "HookChain_…",
  "indexedPredicates": [
    { "path": "AttackerId", "operator": "Equals", "value": "player-1", "valueType": "string" },
    { "path": "Damage", "operator": "GreaterThanOrEqual", "value": 5, "valueType": "int" }
  ],
  "indexCoversPredicate": true
}
```

## Extraction rules (v1)

Extraction happens in `HookChainIndexPredicateExtractor`
(`src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/`), reusing the same member/constant
detection the IR lowering already performs. Two sources feed it:

- **Inline `.Where(...)` chains** — `Extract(...)` walks the chain stages.
- **Kernel-class `ShouldHandle` bodies** (issue #51, candidate 1) — `ExtractFromShouldHandle(...)` treats an
  expression-bodied predicate (`=> …`) or a single `return …;` block exactly like a `.Where(...)` lambda.
  `PluginKernelModelFactory` feeds it the lowered kernel's `ShouldHandle`, so kernel-authored subscriptions
  now ship the same index metadata inline chains do. (Multi-statement bodies, live-setting/captured-field
  comparisons, and any non-constant leaf stay non-indexed, exactly as before.)

A leaf is index-eligible when it is `event-property <op> compile-time-constant`:

- Operators: `==`, `!=`, `>`, `>=`, `<`, `<=`.
- Operands are **normalized** so the event property is the left operand
  (`5 >= e.Damage` ⇒ `Damage <= 5`).
- The constant must be resolvable with `GetConstantValue()` (literals and `const`); runtime-captured
  locals are not index values.
- Only leaves reachable through **top-level `&&`** are emitted, so every `IndexedPredicate` is a
  *necessary* AND condition of the real predicate. This is what makes host rejection on any single
  predicate always sound.

`IndexCoversPredicate` is `true` only when the whole predicate reduces to that conjunction — no `||`,
no `!`, no `.Where()` after a `.Select()`, no non-constant or non-property leaf, no unsupported type.
Anything else conservatively forces partial coverage (`false`) and the un-indexable parts remain in
the verified IR.

## Framework index (issue #50)

The matcher and its attribute are first-class framework types in `DotBoxD.Plugins.Indexing`, so no host
reimplements them:

- `EventIndexKeyAttribute` — a host marks the event properties it indexes.
- `EventIndexMatcher<TEvent>` — compiles manifest predicates into cheap checks over **precompiled property
  getters** (expression-tree delegates built once per closed generic, never per-event reflection). It honors
  only `[EventIndexKey]` paths and reconciles each predicate value to the property's CLR type; a value whose
  type can't be reconciled (or a comparison it can't decide) is left to the verified IR rather than turned
  into a throwing or unsound check.
- `EventIndexRegistry` / `EventIndexStats` — register a subscription + its predicates, then `Publish` events;
  the registry prefilters via the matcher and dispatches survivors to the verified IR, exposing
  considered/prefiltered/dispatched counters. `Register` returns `false` when no predicate path is an index
  key, so the host keeps that subscription on its broad pipeline.

## How a host uses it (correctness fallback)

1. **Register**: `EventIndexRegistry.Register(adapter, kernel, IndexedPredicates, IndexCoversPredicate)`.
2. **Prefilter**: `Publish(event)` evaluates the cheap index checks. Any definitive failure ⇒ skip dispatch
   entirely (no sandbox entry).
3. **Fallback**: a survivor runs the verified IR predicate **unless** the index fully covers it
   (`IndexCoversPredicate` true *and* every predicate path is an honored index key, doubles excluded). The
   verified IR stays the source of truth; the index is only an optimization.

## Sample demonstration (`samples/GameServer`)

- `AttackEvent` marks `AttackerId`, `TargetId`, `Damage` with `[EventIndexKey]` (the framework attribute).
- `GameWorld` owns an internal `EventIndexRegistry` (issue #49); `GamePluginControlService.WireSubscription`
  routes any subscription that carries honored index metadata into it instead of the broad pipeline, and
  `GameWorld` publishes each event through both the broad pipeline (non-indexed subscriptions) and the index
  registry (indexed ones). Live dispatch now actually reduces fan-out — the smoke run still calms/taunts.
- On install the server still logs what it indexed (`[server] registered indexed subscription: …`).
- `EventIndexFanoutTests` + `EventIndexRegistryTests` publish 100 attacks where only 3 share the indexed
  bucket and prove the verified IR ran exactly 3 times — the other 97 never entered the sandbox.

## Non-goals / follow-ups

- **Done**: live GameServer dispatch through the index (#49), framework-level matcher + registry (#50),
  kernel-class `ShouldHandle` extraction (#51 candidate 1).
- **Blocked on lowering, not implemented** (remaining #51 candidates): *nested property paths*
  (`e.Inner.Field`) — events are validated to scalar-only properties and both lowering and extraction accept
  only flat `e.Property`, so the model can't carry nested objects today; and *captured/effectively-constant
  locals* — the IR lowerer rejects non-inlined local identifiers and a runtime value can't be baked into a
  compile-time `IndexedPredicate.Value`. Both need a lowering/event-model change first.
- *Optional*: integrating the prefilter directly into `SubscriptionPipeline`/`HookPipeline` (the registry is
  the standalone alternative shipped here).
