# GameServer plugin sample — dev-ergonomics review

Companion to [interface-driven-plugin-server.md](interface-driven-plugin-server.md) and [plan.md](plan.md).

**Scope:** the developer ergonomics of the GameServer sample — what a *plugin author* writes and codes
against. Three questions drove it: is there boilerplate to remove, is there *false control* that breaks
easily, and is the control too coarse / wrong-grain. **Date:** 2026-06-16.

**Method.** Three independent passes were reconciled: (1) a manual read of the dev-facing surface; (2) a
Codex pass that also built/ran the sample and read the generator internals; (3) a multi-lens verification
workflow (5 ergonomics lenses → adversarial verification of every finding, 46 agents, **35 of 41 findings
confirmed**). Severities below are the adversarially-adjusted ones. Attribution: **[3×]** = all three passes
agreed; **[codex]** = Codex-originated; **[wf]** = the workflow surfaced or sharpened it.

> **Note on status.** The sample is the vibe-check artifact described in the companion design doc — it
> intentionally does not compile (generated halves absent). Several findings are sample-hygiene; the
> load-bearing ones are framework/generator-shape issues that survive implementation.

---

## Verdict

The unified one-interface / three-consumers core is sound. The ergonomic debt clusters in two places:
**control-plane verbs leaking onto the domain contract** (false control), and a **golden sample that demos
every advanced feature at once** (learnability). Fix those two and the rest are small cleanups.

> **Method principle — "asserted in a test" is not a requirement.** A value pinned by a test describes the
> current design; it is not a reason to keep that design. Where a finding's fix was held back because "X is
> asserted in N tests," re-ask *do we need X at all, and is there a better way?* The kernel **identity** below
> is the worked example.

### Identity note — derive ids, don't hand-type them

A server-extension/event kernel currently carries up to five identities: the kernel **type**, a hand-typed
**id string** (`"monster-killer"`/`"guardian"`), a **service interface** (`IMonsterKillerService`), a **graft
target** (`IMonsterControl`), and a **method name**. Only the type is load-bearing — every other one is
derivable from it or redundant:

- The **id string** is the manifest `PluginId` (wire/routing key). It only needs to be computable on the
  plugin side and readable on the server side, and both share the kernel type — so derive it from the type
  (the generator already does this for `PackageName`). Rename-stability is moot (plugin + server compile
  against the same abstractions; the id only has to agree within one install session), readable logs come free
  from the kebab'd type name, and multiple-instances-per-type is unaddressable anyway because `Replace`/
  `Extend`/`Get`/`PluginId` are all keyed by type. Keep an **optional** override only for protocol-pinning.
- The **service interface** (1.2) is never implemented or injected — key `Extend`/`PluginId` off the kernel
  type and it vanishes.
- The **method name** literal (1.3) already defaults to the method's own name.

Net authored surface for a batch kernel: one class marker `[ServerExtension(typeof(IMonsterControl))]` (the
graft target lives on the **class**, not the method) + one bare `[ServerExtensionMethod]` whose name defaults
to the method, zero hand-typed ids, zero invented interfaces. This is the keystone that unblocks the 1.3
collapse and dissolves 2.4.

---

## 1. Boilerplate to remove

| # | Sev | Finding | Fix |
|---|-----|---------|-----|
| 1.1 | med | **[codex] Per-plugin `.csproj` plumbing** — analyzer ref + Fody `WeaverFiles` ref + `Fody` package + `InterceptorsNamespaces` props hand-wired (`Examples.GameServer.Plugin.csproj:4–5,18–19,23`). | Collapse into one `DotBoxD.Plugins.Sdk` props/package import. **Caveat:** the `<InterceptorsNamespaces>…DotBoxD.Plugins.Generated</InterceptorsNamespaces>` line (:19) is load-bearing — omit it and every `InvokeAsync` compiles clean and throws at runtime (see 2.5). Absorb it into the SDK so the dev can't drop it. |
| 1.2 | med | **[wf] The `IMonsterKillerService` placeholder interface** (`MonsterKillerKernel.cs:3–6`) exists only to be named in `Extend<IMonsterKillerService,…>()` / `PluginId<…>()`; never implemented or injected; re-declares the signature already on the kernel method. | Key `Extend`/`PluginId` off the kernel type (`Extend<MonsterKillerKernel>()`); the interface vanishes. |
| 1.3 | med | **[3×] Server-extension annotation triple** (`MonsterKillerKernel.cs:22–30`) repeats `typeof(IMonsterControl)` twice, restates the method name, and carries a hand-typed install id `"monster-killer"`. | **Derive the install id from the kernel type** (kebab of the type name minus `Kernel`, exactly as 1.7 proposes for `[Plugin]` and as the generator already does for `PackageName`); keep an *optional* string override for protocol-pinning. With the id derived **and** the placeholder interface removed (1.2), the whole triple collapses to one class marker `[ServerExtension(typeof(IMonsterControl))]` (graft target on the class) + one bare `[ServerExtensionMethod]` whose name defaults to the method. See the **Identity note** below — the earlier "don't collapse, the id is asserted in tests" caution was wrong: the tests describe the hand-typed design, they don't require it; they should assert the *derived* id. |
| 1.4 | low–med | **[codex] Hosting ceremony** — `Server/Program.cs:80–101` (pipe + 2 `TaskCompletionSource`s + session + provision + disconnect + launch); plugin `args`/usage parse (`Plugin/Program.cs:18–25`). | Small `GamePluginHost`; opt-in `GamePluginServerHost.RunFromArgs(args, …)` helper — **not** a generated `Main` (would bake in the single-pipe assumption tension E defers). |
| 1.5 | low | **[3×] `.ConfigureAwait(false)` ×13 in the plugin `Main`** (`Program.cs:20–93`) — no sync context; sibling kernels and the doc's §7 snippet use bare `await`. | Strip it. First thing a dev copies. |
| 1.6 | low | **[3×] Stale test double** — `RecordingGamePluginControlService.cs:83–112` implements `KillMonsterAsync`/`IsMonsterAsync`/`GetEntity*` no longer on the trimmed `IGamePluginControlService`. Dead, unasserted. | Delete; domain assertions already flow through `FakeWorld`. |
| 1.7 | low | **[wf] pluginId literals** (`"guardian"`/`"retaliation"`/`"monster-killer"`) equal the kebab type name minus `Kernel`. | Make the id optional, default to that; keep the string overload for renames. |
| 1.8 | low | **[wf] `GamePluginServer` shell doc-comment** teaches the generator's job; the one author-actionable line (implement/delete `OnConfigured`) is buried, and the sample `OnConfigured` body is a `Console.WriteLine` a reader copies as wiring. | Lead with the author instruction; demote the demo to a comment. Shell itself is load-bearing (names the builder, fixes the world type) — keep it. |

## 2. False / fragile control that breaks easily

### 2.1 — HIGH — `SetValuesAsync` looks like instance mutation, records a draft scrape **[wf]**

`Program.cs:42–44` reads like "mutate the live kernel". The generator does
`var draft = new TKernel(); set(draft);` then reflects `[LiveSetting]` props and `Convert.ToString`s them
(`PluginServerFacadeEmitter.cs:188–196`). Consequences, all invisible on the happy path:

- reading `k.AggroRange` inside yields the **ctor default, not live state**;
- `k.X += 1` operates on the default;
- setting any non-`[LiveSetting]` property is **silently dropped**;
- method calls / side-effects in the lambda are no-ops.

No test exercises it and the test double discards the payload, so it is fully unguarded.

**Fix:** an expression setter builder — `Get<GuardianKernel>().Set(k => k.AggroRange, 6).Set(k => k.CalmStrength, 35).ApplyAsync(atomic: true)` —
so "read k.X" is unrepresentable and only live keys compile.

### 2.2 — HIGH — Install verbs on the domain contract (the headline; doc tension B) **[3×]**

`IGameWorldAccess : IServiceControl`, controls `: IExtensibleControl` put `Replace`/`Extend`/`Get`/
`ServerExtensions` on the domain surface as **default-interface members that throw**, forcing the server to
implement-and-throw (`GameWorldAccess.cs:35–42,98–107`). Two precisions past the doc:

- the throwers are *also* DIMs **on the interfaces themselves**, which is why `FakeWorld` compiles without
  `ServerControlBase` — removing `ServerControlBase` alone won't fix it;
- the real surprise seat is a **server-extension kernel**: `MonsterKillerKernel`'s injected `_world` is
  statically `IGameWorldAccess`, so `_world.Monsters.Extend(…)` autocompletes beside the domain reads and
  throws — and that author is a plugin dev.

**Fix:** make `IGameWorldAccess`/controls pure domain (drop the bases) and move install verbs to the
builder's build-time `Setup(...)` accumulator. `Build()` records `Replace`/`Extend` intents synchronously;
`StartAsync()` ships them. The runtime `server.Monsters` property is exactly `IMonsterControl`, so
`server.Monsters.Extend(...)` becomes a compile error and `s.Monsters.Extend(...)` is the only install shape.

### 2.3 — HIGH — `Replace`/`Extend` are less type-safe than they look **[codex, verified]**

`Extend<TService,TKernel>` has **no** `TKernel:TService` constraint and just stores
`_serverExtensions[typeof(TService)] = pluginId` (`PluginServerFacadeEmitter.cs:161–166`); `Replace` ignores
`TService` at runtime (wiring is by manifest subscription). Nothing validates the kernel against `TService`.

**Fix:** analyzer/runtime validation that the kernel manifest actually exports/implements `TService`.

### 2.4 — HIGH — Plugin-id ↔ type duality silently mis-targets settings **[wf]**

`[Plugin("guardian")]` (string) vs `Get<GuardianKernel>()` (type). The generator's duplicate detector keys on
the **type name, not the `[Plugin]` id**, so two kernels both `[Plugin("guardian")]` produce the same manifest
PluginId, last-install-wins, and `Get<T>` silently tunes the wrong kernel — with no check that `T` was ever
installed.

**Fix:** **derive the id from the kernel type** rather than hand-typing it (see the Identity note). That
dissolves most of this by construction — nobody can collide on a `"guardian"` literal nobody types, and the
compiler already forbids two types with the same FQN. Residual (two *derived* ids clashing because the
namespace was kebab'd away) is caught by keying the existing duplicate detector on the derived id — a cheap,
now-rare check instead of a silent footgun. Constrain `Get`/`Replace`'s `TKernel` to carry the kernel marker
so the wire id is always taken from the type, never a re-typed literal.

### 2.5 — med — `InvokeAsync` un-intercepted → runtime throw, not build error **[3×, verified]**

The emitted body throws "must be intercepted" (`PluginServerFacadeEmitter.cs:155–158`); `Program.cs` calls it
directly. **Correction:** interception is C# `[InterceptsLocation]`, **not Fody** — Fody only optimizes the
implicit-capture reflection, and weaver-absent means slower reflection, not a silent break. The real footgun is
the missing `<InterceptorsNamespaces>` csproj line → clean compile, runtime throw. The diagnostic the design
*specifies* ("emit a build diagnostic when interception location is null") is **not implemented** —
`InvokeAsyncModelFactory.cs:134–138` returns null silently.

**Fix:** implement that DBXK diagnostic, message pointing at the exact csproj line; keep explicit capture-bag
as the golden path, demote implicit capture.

### 2.6 — low — Throwing property getters are debugger-hostile and mis-message **[wf]**

`server.Monsters` throws before `StartAsync` (`PluginServerFacadeEmitter.cs:82–84,132–133`). IDE debuggers
eagerly evaluate properties, so *hovering* `server` fires the exception; and `server.Monsters.KillAsync(…)`
fails on the `.Monsters` access. In the `FromConnection(control)` no-world path the message says
"Call StartAsync()" when StartAsync isn't the fix.

**Fix:** `[DebuggerBrowsable(Never)]` + a `[DebuggerDisplay]` that doesn't trip the gate; split the message by
cause (not-started vs no-world-supplied).

### 2.7 — low — `CalmStrength` was `string "20"` among `int [Range]` siblings **[3×]**

`GuardianKernel.cs`. The framework now supports deterministic, culture-invariant `int→string` lowering for
kernel interpolation, so `CalmStrength` can be a numeric live setting again:
`[LiveSetting] [Range(0, 50)] public int CalmStrength { get; set; } = 20;`. The kernel still emits it into the
`calm:<player>:<strength>` host message, but DBXK100 now only rejects unsupported interpolation hole types;
`int` holes are converted through the invariant IR helper. The server-side clamp can remain a defense-in-depth
boundary, but the authored sample no longer has to give up range metadata to pass the strength through a
message.

## 3. Too coarse / wrong-grain control

### 3.1 — med — Control split fights the capability tree **[wf]**

The dev navigates `Monsters` vs `Entities`, but **all** of `IEntityControl` is gated under
`game.world.monster.read.*` (no `entity.*` subtree exists) while `Monsters.GetThreatAsync` is
`game.world.combat.threat` (`GameWorldAccess.cs:66,78–88`). So `server.Entities.*` costs `monster.*` caps and
one `Monsters.*` member costs `combat.*` — the grouping the dev sees is orthogonal to the policy that gates it.

**Fix (policy layer, server-author owned):** rename entity reads to `game.world.entity.read.*` and add the
matching wildcard grant in `ServerPolicy`, preserving the intentional `GetThreat → combat.threat` exception.
Don't reshape `IGameWorldAccess` (that would force the SDK author to restructure the domain for policy
reasons).

### 3.2 — med — Capability requirements are invisible where install fails **[wf]**

By design caps live on the server impl (`GameWorldAccess` `[HostBinding]`), an assembly the plugin author
never opens. A server-extension kernel can legally `await _world.Monsters.GetThreatAsync(id)` (compiles against
the pure interface) yet **fail closed at install with no local signal**.

**Fix:** analyzer diagnostic at the kernel call site when a referenced method's capability falls outside the
kernel's grantable prefix. Scope to server-extension + `InvokeAsync` kernels (sync event hooks can't await
these).

### 3.3 — low — `WireHook` string switch **[codex]**

`GamePluginControlService.cs:144–155` maps kernel→event by `Subscriptions[0].Event` and throws on anything
unmatched. Mostly server-author pain; surfaces to the plugin author as an opaque install failure.

**Fix:** generate a Type-keyed dispatch from the registered `IEventKernel<T>` set; interim, list the supported
events in the exception message.

## 4. Learnability

### 4.1 — med — Seven ways to move behavior/state in one `Main` **[wf]**

`Replace` / `Extend` / `Get` / direct-RPC + 3 `InvokeAsync` flavors (`Program.cs:35–90`). The two confusable
ones — `Extend` vs `InvokeAsync` — both ship server-side IR over the same world surface; nothing states the
selection rule.

**Fix:** a "which verb when" decision block where the dev meets it, and **split the sample into a short golden
path + an advanced file** (Codex's recommendation).

### 4.2 — med — `[Plugin]` attribute name overload **[wf]**

"Plugin" is simultaneously the facade (`GamePluginServer`), the marker (`[GeneratePluginServer]`), the assembly
(`.Plugin`), *and* the event-kernel role (`[Plugin("guardian")]`) — sitting beside `[ServerExtension]` for the
sibling role.

**Fix:** rename the attribute to `[Plugin]`/`[ServiceKernel]` so the two kernel roles read as a matched
pair and "Plugin" means the deployable unit. (The naming-decision doc deferred *class* renames but never
reconciled this *attribute* overload.)

### 4.3 — low — Two authoring models, one `…Kernel` suffix **[wf]**

`[Plugin]` sync event kernels vs `[ServerExtension]` async injected kernels. The discriminator (the attribute)
is fine; document the fork at the two install lines in `Program.cs`. **Don't** rename the classes —
concept-naming-decision.md deliberately deferred that.

---

## Do-first (priority)

1. **Pure-domain `IGameWorldAccess`** — move install verbs to build-time `Setup(...)` accumulators; delete the
   throwers *and* the DIM throwers so `server.Monsters` and a kernel's injected `_world.Monsters` both show
   only domain calls. (2.2)
2. **Derive kernel ids from the type** (Identity note) — kill the hand-typed `"monster-killer"`/`"guardian"`
   strings, key the duplicate detector on the derived id, and validate `Replace`/`Extend` against the manifest.
   This is the keystone: it dissolves 2.4 and unblocks the 1.3 collapse. (1.3, 2.3, 2.4)
3. **Expression-based `SetValuesAsync`** — kills the draft-mutation footgun. (2.1)
4. **Split the sample** (golden vs advanced) + a verb decision-block; demote implicit-capture `InvokeAsync`.
   (4.1, 2.5)
5. **Collapse `.csproj`/Fody/interceptor plumbing into an SDK** *and* implement the missing un-intercepted DBXK
   diagnostic. (1.1, 2.5)
6. **Cleanups (mostly free once #2 lands):** drop the placeholder `IMonsterKillerService` and the redundant
   `[ServerExtensionMethod]` name (the triple collapses to one class marker + one method marker),
   `ConfigureAwait` noise, the stale test-double methods; rename `[Plugin]`. Keep `CalmStrength` as `int`
   with `[Range(0,50)]`; the kernel IR now supports invariant `int→string` interpolation for the host message.

---

## Ruled out (do not re-chase)

The adversarial pass rejected 6 findings — including two that earlier passes had leaned toward:

- **Unused `HookContext ctx`** on the batch method — *not* a defect. It's a uniform framework-wide lowering
  marker (used in the event hooks via `ctx.Messages.Send`), and the canonical SDK example keeps it even with
  constructor injection. Making it conditional on body usage would worsen ergonomics.
- **"`atomic:` bool is too coarse"** — rejected. Batch validation is **unconditional** (whole batch validated
  up front, one invalid value aborts before any store); `atomic` only controls the kernel execution gate, not
  validation atomicity. Residual is purely cosmetic (bool vs named enum).
- **"Fody gates `InvokeAsync`"** (Codex framing) — wrong mechanism; it's `[InterceptsLocation]`. The writeback
  is generated C#, so weaver-absent = slower reflection, not a silent break.
- **Capture-bag "silent field drop"** — rejected. Data-flow drives sync-out over the actual `bag.X = …`
  statements; unsupported shapes fail at build, not at runtime.
- **`GetThreat` "no surface hint"** — rejected as stated: the XML doc *is* on the method. The better-framed
  capability-invisibility finding (3.2) survives instead.
- **`Kernels/` namespace mismatch** — rejected: `Program.cs` has an explicit `using`; nothing resolves "by
  accident", not fragile.
