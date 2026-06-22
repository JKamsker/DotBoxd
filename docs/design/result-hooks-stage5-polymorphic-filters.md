# Follow-up: Stage 5 — polymorphic combatant filters for result hooks

> Status: **shipped / implemented**. Stages 0–4 of the
> result-returning hooks plan (`Hooks.On<TContext>()` with `Register`/`RegisterLocal`, the `[HookResult]`
> builder generator, object-initializer and fluent-builder lowering, runtime dispatch) and the game-server
> combat sample shipped first; this document specced Stage 5 and the **v1 scope it describes is what landed** —
> the `[PolymorphicHandle]`/`[HandleSubtype]` attributes, the analyzer declaration-pattern lowering, the runtime
> metadata readers, and end-to-end tests (`CombatPolymorphicSampleTests`) all exist. Follow-up issue #79 broadened
> the original v1 subset to support declaration captures in `||` branches, conditional true branches, key-property
> recursive patterns such as `{ Id: > 0 }`, and multiple declaration captures in one `&&` filter while keeping
> unsupported capture escapes fail-safe.

## Goal

Lower the polymorphic combatant filter shape from the plan:

```csharp
context.Server.Hooks.On<CombatDamageContext>()
    .Where(ctx => ctx.Attacker is PlayerCombatant a && a.HasEquippedItem(itemRuntimeId))
    .Where(ctx => ctx.Victim is MonsterCombatant m && m.IsBoss)
    .Register(ctx => CombatDamageResult.Ok().WithDamage(ctx.Damage * 2), priority: 100);
```

with the declarative metadata:

```csharp
[PolymorphicHandle(keyMember: nameof(Id))]            // designates the scalar handle key
[HandleSubtype(typeof(PlayerCombatant),  discriminator: "player",  bindingPrefix: "combatant.player",  capability: "combatant.player.read")]
[HandleSubtype(typeof(MonsterCombatant), discriminator: "monster", bindingPrefix: "combatant.monster", capability: "combatant.monster.read")]
public abstract record Combatant(long Id);

public sealed record PlayerCombatant(long Id) : Combatant(Id)
{
    [HostBinding("combatant.player.hasEquippedItem", "combatant.player.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    public bool HasEquippedItem(long itemRuntimeId) => throw HostOnly();
}
```

**Current scope (keep bounded, fail safe otherwise):** support `expr is T local`, chained `&&` filters with one or
more declaration captures, declaration captures inside either branch of `||`, key-member recursive patterns
(`expr is T { Id: > 0 } local`), and a simple declaration-pattern condition whose `?:` true branch uses the capture.
The captured local may be used only while the C# pattern scope is definitely guarded, and only as the receiver for
instance `[HostBinding]` calls (`local.Method(args)`) or as the carried key. Capture escapes into later lambdas,
unguarded branches, or unsupported pattern forms still fail safe — the chain does not lower and the existing
un-lowered diagnostic (DBXK110/111/113) fires.

## Why this was a from-scratch subsystem

When this document was written there was **no** supporting infrastructure (all of the following was added as the
stage shipped):

- no `[PolymorphicHandle]` / `[HandleSubtype]` attributes;
- no notion of an event property that is a **handle** (a host-resolved key) rather than a marshalled value —
  `ConventionEventAdapter` marshals every property through `KernelRpcMarshaller`, which rejects an abstract
  `Combatant`;
- no declaration-pattern (`is T local`) handling in `DotBoxDPatternExpressionLowerer`;
- no discriminator host-call lowering;
- no scoped instance host-call (`local.Method(args)` threading the receiver key) — the `Get(key).Method()`
  mechanism referenced in older notes does not exist here.

So Stage 5 is new code across the abstractions, the event adapter, the analyzer, and the sample — not wiring.

## Design: the handle is a host-resolved scalar key

The whole subsystem reduces to one idea: **a `[PolymorphicHandle]` value is carried through the sandbox as its
scalar key** (the `keyMember`, e.g. `long Id`), and the discriminator and subtype methods are ordinary
**host bindings that take that key** and resolve the entity host-side. This reuses the existing host-binding
pipeline (capability collection, effect union, install-time gating) and avoids any new runtime "handle registry."

| Authoring                                   | Lowers to (kernel IR)                                                            |
|---------------------------------------------|---------------------------------------------------------------------------------|
| `ctx.Attacker` (`[PolymorphicHandle]` prop) | `Var("e_Attacker")` — the key scalar the adapter produced                        |
| `ctx.Attacker is PlayerCombatant a`         | `CallExpression("combatant.player.is", [e_Attacker]) -> bool`; binds `a = e_Attacker` |
| `a.HasEquippedItem(itemRuntimeId)`          | `CallExpression("combatant.player.hasEquippedItem", [a, itemRuntimeId]) -> bool` |
| `(x is T a) && rhs`                         | `And(discriminator, rhs[a := key])`                                             |

The `discriminator: "player"` + `bindingPrefix: "combatant.player"` give the binding ids
`combatant.player.is` (the type test) and `combatant.player.<method>` (the subtype methods).

## Components and exact files

1. **Attributes — `src/Hosting/DotBoxD.Abstractions/HookContracts.cs`**
   - `PolymorphicHandleAttribute(string keyMember)` on `Class | Struct`.
   - `HandleSubtypeAttribute(Type subtype, string discriminator, string bindingPrefix, string capability)`,
     `AllowMultiple = true`.
   - Public API → refresh `docs/api-baselines/DotBoxD.Abstractions.txt`.

2. **Generation-name constants — `…/Analysis/Lowering/DotBoxDGenerationNames.cs`**
   - `TypeNames.PolymorphicHandleAttribute` / `HandleSubtypeAttribute` + `Metadata` aliases, and mirror them in
     `tests/.../PluginAnalyzer/Contracts/PluginAnalyzerTypeNameContractTests.cs` (the set-based contract test —
     this is the gate that already caught a missing mirror once).

3. **Event-property-as-handle (analyzer) — `…/Analysis/PluginSymbolReader.cs` (`EventProperties`)**
   - When a property type carries `[PolymorphicHandle]`, emit its `EventPropertyModel` with the **key member's**
     scalar manifest tag + SandboxType (not the record's). The downstream lowering then reads `ctx.Attacker` as
     that scalar via the existing `Var("e_Attacker")` path — no lowering change needed for the read itself.

4. **Event-property-as-handle (runtime) — `…/Runtime/PluginEventAdapterRegistry.cs` (`ConventionEventAdapter`)**
   - For a `[PolymorphicHandle]` property, build the getter to return `value.<keyMember>` and the parameter
     `SandboxType` from the key member's type. (Reflect the `keyMember` once in the ctor.) A custom adapter can do
     the same; this only teaches the auto-adapter.

5. **Declaration-pattern lowering — `…/Analysis/Lowering/DotBoxDPatternExpressionLowerer.cs`**
   - Add a `DeclarationPatternSyntax` arm: if the matched value is a handle and `T` is a `[HandleSubtype]` of its
     `[PolymorphicHandle]` base, emit the discriminator `CallExpression(prefix + ".is", [value])` and register the
     declared identifier (`a`) in the lowering context bound to the handle key IR + the subtype.
   - Collect the subtype `capability` into the manifest.

6. **Binding the pattern variable — `…/Analysis/Lowering/Expressions/DotBoxDExpressionLoweringContext.cs`**
   - Add a small map `name -> (DotBoxDExpressionModel key, INamedTypeSymbol subtype)` for in-scope pattern
     captures. Captures are threaded only into branches where C# definite pattern scope keeps the discriminator
     guard in force: later operands in the same `&&` chain, the containing `||` branch, and a simple `?:` true
     branch. `LowerIdentifier(a)` returns the bound key IR.

7. **Scoped instance host-call — `…/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs`**
   - Recognize `local.Method(args)` where `local` is a bound pattern capture and `Method` is an instance
     `[HostBinding]` on the subtype: lower to `CallExpression(bindingId, [localKey, ...args])` — the receiver key
     threaded as the leading argument. (Generalizes the current static-receiver host-call path.)

8. **`&&` short-circuit shape — `DotBoxDExpressionModelFactory.LowerBinary`**
   - Ensure `(x is T a) && rhs` lowers `x is T a` first (introducing the capture), then `rhs` with `a` in scope.
     Multiple declaration captures in one `&&` chain are threaded left-to-right. Captures must not escape their
     guarded C# branch or lambda (fail safe if `a` is used elsewhere).

9. **Runtime host bindings (sample/host) — sample or host registration**
   - Register `combatant.player.is`, `combatant.player.hasEquippedItem`, `combatant.monster.is`,
     `combatant.monster.isBoss`, each taking the key (+ args) and resolving the entity from host world state. These
     are ordinary `HostServiceBindingFactory` bindings; no new runtime type.

10. **Sample + tests**
    - Extend the combat sample with a `Combatant`/`PlayerCombatant`/`MonsterCombatant` hierarchy and the host
      bindings; add the Divine Sword / Boss Shield plugins in the polymorphic form and assert parity with the
      context-field form already shipped.
    - Analyzer tests: `is T local && local.HostMethod()` lowers (discriminator + scoped call present); unsupported
      pattern shapes fail safe with the un-lowered diagnostic; missing `[HandleSubtype]` fails safe.

## Risks and mitigations

- **Adapter / symbol-reader regression (highest):** items 3–4 touch code on the path for *all* events. Mitigate
  by gating strictly on `[PolymorphicHandle]` and adding adapter round-trip tests for a non-handle event next to a
  handle event.
- **Capability/effect mismatch → install fails DBXK041:** the discriminator and subtype bindings must declare the
  same effects on the analyzer and runtime sides; reuse `DotBoxDHostBindingExpressionLowerer`'s existing
  classification rather than hand-rolling.
- **Pattern-scope leaks:** a captured `a` used outside its `&&` must fail safe, not miscompile. Cover with a
  negative test.
- **Key type breadth:** v1 should support `long`/`int`/`Guid`/`string` keys (all marshaller-eligible); reject
  others.

## Test plan

Analyzer/codegen: discriminator + scoped-call lowering; capability collection; each fail-safe shape.
Runtime: the two sample plugins in polymorphic form produce identical outcomes to the context-field form.
Parity: a handle event and an ordinary event in the same suite (adapter regression guard).

## Effort

Roughly a focused day: ~8 source files + sample + ~12 tests, plus the Abstractions API baseline and the
TypeNames contract-test mirror. The lowering (items 5–8) is the subtle part; the adapter/symbol-reader changes
(3–4) carry the regression risk and deserve the most test attention.

## Alternative considered (and why not)

A true opaque-handle value kind in the sandbox (a new `SandboxValue` subtype + VM/codec/marshaller switches) was
rejected: per the "Sandbox value kind checklist," a new value kind is ~9 VM switches plus codec/converter/
marshaller/emitter work — far larger than the key-scalar model, with no added capability for this use case.
