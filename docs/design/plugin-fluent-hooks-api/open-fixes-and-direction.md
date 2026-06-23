# Plugin context & hooks: open fixes and design direction

Companion to [server-walkthrough.md](server-walkthrough.md),
[plugin-walkthrough.md](plugin-walkthrough.md),
[../remote-plugin-server-builder/interface-driven-plugin-server.md](../remote-plugin-server-builder/interface-driven-plugin-server.md),
and [kernel-binding-model.md](kernel-binding-model.md).

**Status:** Design direction + review backlog for PR #88 (`codex/improve-hooks-issue-87`, issue #87),
revised after a multi-lens review (see [How this doc was reviewed](#how-this-doc-was-reviewed)).
**Date:** 2026-06-23. **Observed PR head:** `41ec9172`.

Design guide this doc is measured against: **Simple · Obvious · Discoverable · Consistent · Minimal ·
Composable** — plus **Explicit · Stable · Testable** as working corollaries.

**Citation convention:** every code reference names a file and an exact line or line range as of head
`41ec9172`. A bare line (`:86`) is the precise statement; a range (`:23-27`) spans the full construct. If a
line has drifted when you read this, search the named symbol in the same file.

**Project stance (read first).** This is a single-maintainer project with no external consumers.
**Backward compatibility is a non-goal:** existing plugins, samples, and API baselines are broken outright
to reach the cleanest end shape and to avoid confusing future readers — there is no migration, deprecation,
or shim burden anywhere in this doc. **Discoverability must be local, not name-derived:** a generated symbol
is acceptable only when it is reachable from something the author already wrote — a member on a type they
declared, or an extension method generated into their namespace (so IntelliSense surfaces it in place). A
separately-named generated type the author must *know exists* by a naming rule (`{Server}` ⇒
`{Server}Context`) is the anti-pattern this doc removes.

---

## TL;DR

There are **two independent problems**; this doc keeps them apart on purpose.

1. **Correctness — blocks merge.** PR #88 has **three** correctness bugs (P1.1 factory-collapse, P1.2
   result-hook priority regression, P1.3 `RunLocal`-helper composition) **plus** a red CI smoke failure
   (P1.4). None of the four depends on the context's *shape*; each gates merge on its own.

2. **Shape — a separate, larger thesis.** The generated plugin context
   ([PluginServerContextSurfaceEmitter.cs:16](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerContextSurfaceEmitter.cs))
   is the **only generated type the author extends by hand at a name they must already know** —
   `{Root}Context`, derived from the server class name, with nothing they wrote pointing to it
   ([GamePluginContext.cs:3-10](../../../samples/GameServer/Examples.GameServer.Plugin/GamePluginContext.cs)).
   The fix is to make the context **author-declared** (you name the type; you extend the type you named —
   §3.1), removing the convention-named partial. One hard constraint: `[KernelMethod]` cannot live on an
   interface (its body must be inlined), so the declared context is a `partial` **class** (§3.1). Lands as
   its own PR(s) after §3.1's (now small) decisions are settled.

One-line direction: **fix P1.1–P1.4 first; make the context surface declarable only after answering §3.1;
fix chain/context identity by ownership (§3.3); single-source the host-capability derivation rule without
removing the host's independent install-time recomputation (§3.4).** Throughout, the **audience split** is
load-bearing: the server author ships an SDK; the plugin dev consumes it and **safely extends** it with
`[ServerExtension]` — see [The two audiences](#the-two-audiences-and-the-plugin-dev-extension-surface).

---

## The two audiences (and the plugin-dev extension surface)

Two roles, one SDK boundary:

- **Server author** ships an **SDK** (one package): DotBoxD runtime/abstractions + the domain contracts
  (`[DotBoxDService] IGameWorldAccess` and its handle/control types `IMonster`, `IMonsterControl`, …), the
  event types, the host capabilities (`[HostCapability]`), and the generated client facade
  (`server.Hooks`/`Subscriptions`, the context, the builder).
- **Plugin dev** references that one package and writes against ready-made types — never seeing DotBoxD
  primitives, IPC, lowering, or binding ids.

The plugin dev's **safe extension surface** — how they add operations to the API *for their own use case*,
server-side, with **no authority escalation**:

| Mechanism | What it does | Safety |
|---|---|---|
| `[ServerExtension(typeof(T))]` + `[ServerExtensionMethod]` | grafts a whole server-side operation onto an existing server-owned type `T` (handle/control/world) as an **extension method in the plugin dev's own namespace** — e.g. `world.Monsters.Get(id).BlinkBehindAsync(player)` | verified sandboxed IR; capability set = union of the host bindings the body calls, **policy-gated at install** (`DBXK044`/deny); cannot exceed granted authority |
| `[KernelMethod]` | a pure scalar **inline helper** (predicate / derived value) used inside a lowered `Where`/`Select`/`Run` | scalar-only; calls only granted host bindings |

`[ServerExtension]` is the primary lever. A grafted method **composes both ways** — callable inside a lowered
hook chain (it lowers and runs server-side, no extra roundtrip) **and** standalone over IPC (Part 2). Its
trailing context parameter is the **generated context** (`GameContext`), not raw `HookContext`; `HookContext`
stays accepted as an escape hatch since the generated context already exposes `.Raw`. The reference example
is `BlinkKernel`
([Kernels/BlinkKernel.cs](../../../samples/GameServer/Examples.GameServer.Plugin/Kernels/BlinkKernel.cs)):
`[ServerExtension(typeof(IMonster))]` injects the addressed monster + the root world, and
`[ServerExtensionMethod] BlinkBehindAsync(string playerId, GameContext ctx)` does a root-world read + a
scoped read, computes, then performs a host write (`TeleportToAsync`). Its capabilities —
`game.world.combat.threat`, `game.world.entity.read.position`, `game.world.monster.write.position` — are
exactly the host bindings it touches, gated at install, so the plugin extends the API **only within its
granted authority**. The generated extension lands in the author's namespace
(`…Kernels.BlinkKernelDirectServerExtensionClientExtensions.BlinkBehindAsync`), discoverable on any
`IMonster` with no convention name to know. `MonsterKillerKernel` is the collection variant (grafted onto a
control for batch/list aggregation).

**Cross-assembly: verified viable.** A plugin is an assembly that references a **prebuilt** SDK and contains
**no** `[GeneratePluginServer]` of its own. The shipped sample collapses facade generation and plugin
authoring into one assembly (`Examples.GameServer.Plugin` holds both `GamePluginServer` *and* `BlinkKernel`),
so it doesn't exercise this — but a two-project probe (a referenced SDK facade + a consumer with no
`[GeneratePluginServer]`) **compiles clean**: the emitted interceptors resolve the *referenced* context type
for the hook chains (the §3.3 return-type resolution works across the boundary), and the `[ServerExtension]`
graft fires on `IMonster` with the correct capability set. No cross-assembly blocker in the normal case.
(P2.5's whole-compilation scan remains the known weak spot for the *multi-server* / fallback case — §3.3.)

**SDK packaging** — flowing `InterceptorsNamespaces` so a single `PackageReference` enables interception is
tracked separately in [#89](https://github.com/JKamsker/DotBoxD/issues/89).

---

## Part 1 — The server-author surface (the good news)

The "server declares the contract; the client writes one line; codegen fills the rest" model **already
exists and is interface-driven** for the RPC-forwarding surface:

```csharp
[GeneratePluginServer]
public partial class GamePluginServer : IGameWorldAccess;
```

From the `[DotBoxDService]` interface graph the generator **enumerates members** and emits the RPC proxy
forwarders, the `IPluginServer<IGameWorldAccess>` lifecycle, the `Setup` accumulator, live-settings `Get`,
`IGameWorldServer`, and `GamePluginServerBuilder`:

| Generated artifact | Driven by | Exact location |
|---|---|---|
| World resolution | the directly-implemented interface carrying `[DotBoxDService]` (not the class name) | [PluginServerFacadeModelFactory.cs:71](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeModelFactory.cs) `ResolveWorldType` |
| Controls / forwarders / scoped clients | walking the interface's members + nested `[DotBoxDService]` returns | same file: `ResolveControls` :89, `ResolveMethods` :125, `ResolveReturnWrapper` :196 |
| `I{World}Server`, lifecycle, builder | the world **type symbol** | [PluginServerFacadeEmitter.cs:20](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeEmitter.cs); `ServerInterfaceName` at [PluginServerFacadeNameFormatter.cs:30](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeNameFormatter.cs) |
| **Context + hook/sub registries** | **the class-name string** `FacadeRootName(name) + "Context"` | [PluginServerContextSurfaceEmitter.cs:16](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerContextSurfaceEmitter.cs); `ContextName` at [PluginServerFacadeNameFormatter.cs:21](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeNameFormatter.cs) |

**Precise framing of the gap.** The context is **not** the only string-derived generated *name*:
`SetupInterfaceName` ([:13](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeNameFormatter.cs)),
`ContextName` (:21), `HookRegistryName` (:24), and `SubscriptionRegistryName` (:27) are all
`FacadeRootName(className) + suffix`, and `ResolveControlService` hardcodes the literal metadata name
`{worldNamespace}.Ipc.IGamePluginControlService`
([PluginServerFacadeModelFactory.cs:87](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeModelFactory.cs),
inside `ResolveControlService` declared at :82) — a baked-in convention literal in the path the table calls
contract-driven. The distinction that actually matters is **discoverability of where you write code**, not whether a name is
string-derived. `server.Hooks` (a member on `server`) and the `ctx` lambda parameter (a type IntelliSense
shows you) are *locally discoverable* — fine. The registries and `Setup` are string-**named** but their
member surfaces are **fully generated shells the author never edits**, so the name is never typed by hand —
also fine. The context is the **one** place this breaks: to extend it the author must hand-write
`partial class {Root}Context`
([GamePluginContext.cs:5,7,9](../../../samples/GameServer/Examples.GameServer.Plugin/GamePluginContext.cs)
add `DamageDecisionReason`, `FormatCalmTarget`, `ScaleDamageDecision`), which requires *knowing* the
convention-derived type name with **nothing they wrote pointing to it**. That "separate symbol whose name
you must already know" is the discoverability anti-pattern; a generated member on a type the author
declared, or an extension generated into the author's namespace, would not be. §3.1 fixes it by making the
context author-declared.

---

## Part 2 — Is the "RPC pipeline" one thing or two?

**It is two dispatch concepts that share the lowering front-end and converge for server extensions, plus
one deliberately separate native terminal.** Treat the analyzer↔runtime seam as a *trust boundary*.

1. **Host call inside lowered IR.** A `Where`/`Select`/`Run` (or a server-extension body) that touches a
   host member lowers to a sandbox `CallExpression(bindingId, args)`
   ([DotBoxDHostBindingExpressionLowerer.cs:89](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs)
   for method form, `:95` `TryLowerProperty` for property form). For an **explicit** `[HostBinding(...)]`
   the `bindingId` is the attribute's first constructor argument (`ExplicitHostBinding` at :126); for an
   **auto** host-service binding (a `[DotBoxDService]` interface method with no `[HostBinding]`) it is
   derived by `HostBindingRoute` as `"host." + ns + type.MetadataName + "." + method.Name`
   (`TryAutoHostBinding` :152 → `HostBindingRoute` :187, formula :192). At **exec** time the interpreter
   resolves the id **per call** against the host-curated `BindingRegistry`
   ([ExpressionEvaluator.Calls.cs:149](../../../src/Kernels/DotBoxD.Kernels.Interpreter/Internal/Expressions/ExpressionEvaluator.Calls.cs)
   `TryGetDescriptor`; unknown id throws at :155) and runs the host's descriptor under its effects,
   `RequiredCapability`, and grant check ([BindingRegistry.cs:44](../../../src/Kernels/DotBoxD.Kernels/Bindings/BindingRegistry.cs)
   `GetDescriptor`). The plugin's IR id is **only a lookup key into a host-owned table**.

2. **Server-extension / `InvokeAsync` request-response.** A whole verified function dispatched **by
   `pluginId`** through `InvokeServerExtensionAsync`, whose internal host calls were effect/capability-checked
   at install. Two codegen factories feed this one runtime concept: `InvokeAsyncModelFactory` synthesizes
   the anonymous entrypoint `"rpcEntrypoint":"Invoke"`
   ([InvokeAsyncModelFactory.cs:122](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/InvokeAsync/InvokeAsyncModelFactory.cs)),
   and `RpcKernelModelFactory` emits named `[ServerExtension]` entrypoints — both via `DotBoxDRpcJsonLowerer`,
   both terminating at `InvokeServerExtensionAsync`. In-process (`ServerExtensionProxy`) and over-IPC (the
   generated client proxy,
   [RpcKernelClientProxyEmitter.cs](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Rpc/Client/RpcKernelClientProxyEmitter.cs))
   differ **only** in transport.

3. **`RunLocal` / `RegisterLocal` IPC terminal.** Push-only, keyed by `subscriptionId`, running a **native
   delegate in the plugin process**; it shares the value marshaller but never reaches the binding registry
   ([RemoteLocalHandlerRegistry.cs:41](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteLocalHandlerRegistry.cs)).
   This is the **trust-boundary exit** — the one place a plugin runs unverified native code — and must
   remain a distinct, non-substitutable path.

**The seam, and why it is a cross-check rather than redundancy.** The **auto-binding route** and the
**effect/allocation classification** are derived in **both** assemblies:

- route: `type.MetadataName` in the analyzer
  ([DotBoxDHostBindingExpressionLowerer.cs:192](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs))
  vs `type.Name` in the runtime
  ([HostServiceBindingFactory.cs:239](../../../src/Hosting/DotBoxD.Plugins/Runtime/Bindings/HostServiceBindingFactory.cs));
- allocation: `ReturnAllocates`/`IsWriteMethod` reimplemented in the analyzer (:238/:226) and the runtime
  (:229/:218), with a literal "Must match HostServiceBindingFactory.ReturnAllocates" comment at
  [DotBoxDHostBindingExpressionLowerer.cs:112](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs).

(An **explicit** `[HostBinding("id", …)]` is exempt from route drift: both sides read the same literal id.)

> **This duplication is a claim-vs-ground-truth cross-check, not pure redundancy.** The analyzer output
> ships **inside the plugin package** as the manifest's self-declared effects/capabilities. The runtime is
> the host's own truth: it recomputes the entrypoint's effects from its **own** registry at install
> (`planEffects` at [PluginPreparedPackageValidator.cs:22](../../../src/Hosting/DotBoxD.Plugins/Runtime/Validation/PluginPreparedPackageValidator.cs))
> and raises **`DBXK041`** when `manifestEffects != planEffects` (:23-27 for event kernels;
> [RpcKernelPackageValidator.cs:84-88](../../../src/Hosting/DotBoxD.Plugins/Runtime/Rpc/RpcKernelPackageValidator.cs)
> for server-extension / RPC kernels) and **`DBXK044`** when required capabilities diverge
> ([PluginManifestCapabilityValidator.cs:43-48](../../../src/Hosting/DotBoxD.Plugins/Runtime/Validation/PluginManifestCapabilityValidator.cs)).
> `DBXK041`/`DBXK044` firing on drift is the **security control working**. The system premise — *host frozen
> at release; plugins ship later and independently; the host verifies what ships* — requires the host to
> **recompute**, never to trust the manifest's labels.

**Unification target (precise).** Collapse the **two hand-written copies of the derivation rule** (route +
allocation/effect classification) into **one server-owned definition both sides read**; keep the host's
independent install-time recomputation and the `DBXK041`/`DBXK044` comparison. The win is the check becomes
**un-driftable**, not that it disappears. **Non-goal:** do not merge the two *authorization layers* — a
binding-id call must always resolve through the host-curated `BindingRegistry`
([ExpressionEvaluator.Calls.cs:149](../../../src/Kernels/DotBoxD.Kernels.Interpreter/Internal/Expressions/ExpressionEvaluator.Calls.cs)),
never through plugin-supplied descriptor metadata. (§3.4 explains why this is "one definition, two
projections," not "one shared object.")

---

## Part 3 — The design direction

### 3.1 Make the context surface *author-declared*

Goal: make the context's member surface visible and checkable instead of grafted onto a magic partial.
This is **not** "enumerate an interface like the world surface does," for two verified reasons:

- **Different job.** The world surface *enumerates signatures → emits RPC forwarders*
  (`ResolveMethods` builds `PluginServerForwardedMethod` records,
  [PluginServerFacadeModelFactory.cs:125](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerFacadeModelFactory.cs)).
  The context *wraps an ambient `HookContext` and carries members that lower into IR*. `ResolveControls`/
  `ResolveMethods` read only signatures and have no notion of lowering — they do not transfer.
- **Hard feasibility limit.** `[KernelMethod]` lowering **inlines the method body**: the inliner walks
  `method.DeclaringSyntaxReferences`
  ([DotBoxDKernelMethodInliner.cs:132](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDKernelMethodInliner.cs)),
  requires an expression body (:139) or a single `return` (:146), and throws
  `NotSupportedException("[KernelMethod] '…' must be declared in source.")` at :155 when no body exists. An
  **interface** member has no body ⇒ a pure-interface context **silently fails every `[KernelMethod]`**. The
  design intends `[KernelMethod]` helpers to be **instance methods on the context**
  (`IsServerContextReceiver`, [:36,:70](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDKernelMethodInliner.cs)),
  which an interface cannot carry. **Conclusion:** the declared context is a `partial` **class**, not an
  interface. (Host capabilities are *not* declared on the context — see Ownership below.)

**Discoverability requirement (this is the actual fix).** Make the context type **author-declared and
explicitly attached**, so the place you add members is a type *you* named — not a `{Root}Context` you must
infer:

```csharp
[GeneratePluginServer(Context = typeof(GameContext))]   // proposed attribute arg; explicit, greppable
public partial class GamePluginServer : IGameWorldAccess;

public sealed partial class GameContext { /* your members; obvious where to extend */ }
```

The generator augments `GameContext` (the ambient-`HookContext` wrapping + conveniences) as a partial of
*your* type, and any generated extension methods land **in your namespace** so IntelliSense surfaces them in
place. The convention-named `{Root}Context` partial is **removed**, not kept as a default.

**Remaining decisions (small):**

- **Ownership: decided — option (a).** Three distinct senses, kept separate:
  - **Authority** (what a capability-bearing call may do) is **host-owned, always** — enforced by the
    verifier (unknown-binding rejection + `DBXK041`/`DBXK044`), independent of where anything is declared.
    Not a design choice.
  - **Declaration:** the context is **server-authored and ships in the SDK** — it carries the re-exposed
    server-owned `[DotBoxDService]` services (`ctx.World.Damage.GetAdjustment(id)`, auto-lowering to a host
    binding) plus any server-authored helpers, and **never** `[HostBinding]` members. (`ctx.Messages.Send(...)`
    already works this way — a re-exposed `IPluginMessageSink`, not a plugin-declared binding.) A **plugin
    dev's** own `[KernelMethod]` helpers are **static methods in the plugin's own assembly** (inlined into
    their chains), **not** members on the context — a context compiled in the SDK cannot be extended by a
    `partial` across assemblies. Whole new operations are `[ServerExtension]`, not context members (see
    [The two audiences](#the-two-audiences-and-the-plugin-dev-extension-surface)).
  - **Host-binding metadata** (P2.6, **resolved**): the **implementation** is authoritative; fall back to the
    **interface** when the implementation declares nothing. Both the analyzer and the runtime apply that one
    precedence (impl → interface), defined once in the shared rule (§3.4). Option (a) routes *every*
    plugin-facing host call through the auto-binding path, so single-sourcing that rule is the load-bearing
    follow-up.
- **Shape: a `partial` class, not an interface.** Interface members have no body to inline for
  `[KernelMethod]` (the limit above). Sealed or not is the author's call; `partial` so the generator can
  augment the type the author named.
- **Lifetime/construction: decided.** The generator emits the context constructor (wrapping a `HookContext`)
  into the author's partial **by default**, built per publish as today
  ([HookRegistry.cs:95](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.cs)); an author who needs
  custom construction may **supply a factory** that the generator wires instead.

**No migration.** Per the Project stance, the convention-named partial, the old samples, and the
api-baseline are **broken outright** and regenerated — no overlay, deprecation window, or "keep old code
compiling" path. The extra keystrokes of declaring the context type are **the point**: they make the
extension surface discoverable.

**Security (capability surface — non-negotiable).** Only host-registered bindings grant anything. Under
option (a) the plugin context declares **no** `[HostBinding]` members, so a plugin cannot even *name* a
capability the server did not expose — the assertion vector is closed by construction. The verifier
enforces this regardless of declaration site: any binding id absent from the host catalog is rejected at
validation (unknown-binding) and cannot reach exec
([ExpressionEvaluator.Calls.cs:155](../../../src/Kernels/DotBoxD.Kernels.Interpreter/Internal/Expressions/ExpressionEvaluator.Calls.cs)),
and any effect/capability mismatch is rejected at install (`DBXK041`/`DBXK044`).

### 3.2 Make execution location *explicit*

A context member runs in exactly one of three places. The current signal — an attribute, or its **absence**
— is too implicit; **"no marker ⇒ native" is a footgun.**

| Tier | Marker | Runs where | Body may reference | Author rule |
|---|---|---|---|---|
| Inlined pure helper | `[KernelMethod]` on the context | server-side sandbox (verified IR) | scalars; other `[KernelMethod]` members; re-exposed host-service calls; **no** native services | "pure computation over event fields" |
| Host capability | a re-exposed `[DotBoxDService]` member (`ctx.World.X()`; auto-lowers) — **not** a `[HostBinding]` on the context | server-side host | the host call, gated by its `[HostCapability]` | "reads/writes host/game state" |
| Native | **`[Local]`** (decided; today it is the absence of a marker) | plugin process, post-IPC | arbitrary in-process code | "calls your plugin's own services" |

This table is about **context members** used inside a chain. A whole grafted operation is the separate (and
primary) plugin-extension mechanism — `[ServerExtension]`/`[ServerExtensionMethod]`, see
[The two audiences](#the-two-audiences-and-the-plugin-dev-extension-surface). `[KernelMethod]` here is only
the pure inline helper, not the main way to extend the API.

Two precise corrections:

- **The verbs are not interchangeable.** `Run`/`Register` lower to verified IR (sandbox subset only);
  `RunLocal`/`RegisterLocal` run arbitrary native code. A `RunLocal` body that calls a plugin service does
  **not** become a valid `Run` by dropping the suffix — it fails to lower. Do **not** claim "the same
  expression, the suffix chooses where it runs."
- **Native is opt-in via `[Local]` (decided — option A).** A native (in-process) context member carries
  `[Local]`; execution site is never inferred from a *missing* attribute. The analyzer raises a **build
  error** if a `[Local]` member is used in a lowered stage (`Where`/`Select`/`Run`/`Register`) — a new
  diagnostic alongside the `DBXK111`/`DBXK113`/`DBXK062` family. (Rejected: splitting the context into a
  *lowerable facet* + a *native facet* — more types, less minimal.) The native terminal is the
  trust-boundary exit, so the generator must still route tiers by **owned symbol identity** (§3.3), never by
  string name (P2.4), so a typo or a foreign API cannot route a body to the wrong tier.

**Failure mode (exact).** A non-lowerable member used in a lowered stage compiles, then throws **`DBXK062`**
via `SandboxValidationException` **synchronously at chain construction** — `HookStage.NotLowered()` at
[HookStage.cs:119](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/HookStage.cs) and
`ResultNotLowered()` at :126 — i.e. on first run, not at install. Build-time author detection exists but is
under-leveled: **`DBXK111`** (Remote `RunLocal` not lowered) is `Info`
([AnalyzerReleases.Unshipped.md:10](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md));
**`DBXK113`** (result `Register`/`RegisterLocal` not lowered) is `Info` **except** the un-lowered sandbox
`Register` case, which is already `Warning` at the call site (:12). *Recommendation:* raise `DBXK111` and
the non-`Register` `DBXK113` cases from `Info` to `Warning`. (`DBXK110` is a separate, **stale** diagnostic
— see P2.9; do not conflate the two.)

### 3.3 Fix identity — two *distinct* seams

Both resolve by name/scan today; they are different seams with different fixes.

- **P2.5 — which context type to inject.** `InferredGeneratedContextTypeFullName` scans **every**
  `compilation.SyntaxTrees` ([GeneratedRemoteHookChainFallback.GeneratedContexts.cs:15](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/Support/GeneratedRemoteHookChainFallback.GeneratedContexts.cs))
  and `return null`s on the second distinct `[GeneratePluginServer]` (:34) — so a 2-server project silently
  loses default-context inference. *Fix:* the generated `On<TEvent>()` already returns
  `RemoteHookPipeline<TEvent, {Context}>`
  ([PluginServerContextSurfaceEmitter.cs:57](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerContextSurfaceEmitter.cs)),
  so read the context as `TypeArguments[1]` of the receiver's return type — no scan, no
  `[GeneratePluginServer]` symbol at the call site.
- **P2.4 — is this even a DotBoxD chain.** `CandidateKind`'s fast path
  ([GeneratedRemoteHookChainFallback.cs:23](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/Support/GeneratedRemoteHookChainFallback.cs))
  switches purely on the receiver member's identifier text `Hooks`/`Subscriptions` (:31), with **no**
  semantic check, and the "semantic" fallback `RegistryKind` (:42) is itself only a
  `Name.EndsWith("HookRegistry")`/`"SubscriptionRegistry"` suffix match (:45-46). A foreign fluent API named
  `Hooks`/`Subscriptions` is mis-claimed. *Fix:* gate **both** paths on the receiver's type being **owned by
  a `[GeneratePluginServer]` class**, not on member or type-name strings.

### 3.4 Single-source the host-capability rule — without weakening the check

"§3.4" means **one server-owned definition, two projections**, **not** "one descriptor object consumed by
both." A shared runtime object is not buildable: the analyzer is `netstandard2.0` with zero `ProjectReference`
(packed to `analyzers/dotnet/cs`) and operates on `IMethodSymbol`; the runtime is `net10.0` and operates on
`System.Reflection.MethodInfo`. Therefore:

- Put the binding-id formula and the effect/allocation/classification rules in **one dependency-free source
  file**, linked into both projects via `<Compile Include Link>`, and have both `HostBindingRoute`
  implementations and both `ReturnAllocates`/`IsWriteMethod` implementations call it.
- The shared descriptor must carry, beyond `id` + `effects`: **version, capability, async flag,
  parameter/return shapes, cost, audit, and safety** — these affect stability and drift independently today.
- Replace the **method-name-prefix heuristic** `IsWriteMethod` (names starting `Kill/Set/Update/Delete/Add/
  Remove/Move/Teleport` ⇒ `HostStateWrite`, duplicated at
  [DotBoxDHostBindingExpressionLowerer.cs:226](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs)
  and [HostServiceBindingFactory.cs:218-227](../../../src/Hosting/DotBoxD.Plugins/Runtime/Bindings/HostServiceBindingFactory.cs))
  with an **explicit effect declaration**, so effects are no longer inferred from method names on two sides
  (a method named `Patch` or `Spawn` is silently read-only today on both).
- **Keep** the host's install-time recomputation and `DBXK041`/`DBXK044` (Part 2). One server-owned
  definition makes the check **un-driftable**; it does not remove it.

---

## Part 4 — Open fixes (review backlog)

Every item verified against head `41ec9172`.

### P1 — blockers (correctness; independent of the §3 redesign)

1. **Factory collapse.** `On<TEvent, TContext>(createContext)` validates `createContext`
   ([HookRegistry.cs:82](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.cs)), keys the pipeline
   cache on `PipelineKey(typeof(TEvent), typeof(TContext))` (:86), and on a cache hit (:87) returns the
   existing pipeline — **discarding the just-passed `createContext`**. Two call sites with the same
   `(TEvent, TContext)` but different factories silently share the first factory. *Fix:* do **not** key on
   delegate identity — keep one pipeline per `(event, context)` and **throw on a conflicting factory**. The
   generated convention path binds the static `{Context}.FromHookContext` method group (delegate-equal
   across calls), so it is immune; this bites **hand-written** explicit factories.
   Same pattern in [SubscriptionRegistry.cs:69,73-74](../../../src/Hosting/DotBoxD.Plugins/Runtime/Subscriptions/SubscriptionRegistry.cs).

2. **Result-hook priority is no longer global** once an event has >1 context pipeline. `FireManyAsync`
   ([HookRegistry.Pipelines.cs:59](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.Pipelines.cs))
   iterates pipelines in `Dictionary` order and returns the **first non-null** result (:70-72); priority is
   sorted **only within one slot** — `_order` is an instance field
   ([ResultHookSlot.cs:25](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/ResultHookSlot.cs)),
   incremented per-slot in `Add` (:235), and the sort is per-slot (:240). **So the naive fix fails:**
   merge-sorting by `(priority, order)` cannot order across pipelines because `order` is not comparable
   across slots. *Fix:* introduce a **registry-level monotonic sequence** or an **event-level result table**
   so priority is total across context pipelines. (On `main`, `_pipelines` was keyed by event type alone —
   one pipeline per event — so `FireManyAsync` did not exist; this PR rekeys to `(EventType, ContextType)`
   and adds the multi-pipeline walk, introducing the regression.)

3. **Reusable `RunLocal`/`RegisterLocal` helpers do not compose.** Chain identity is the **call-site source
   location**: `HookChainIdentity.Compute` returns `FNV1a(path + ":" + SpanStart)`
   ([HookChainIdentity.cs:14-19](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/HookChainIdentity.cs)),
   and that id is reused as the **subscription id**, whose registry is **idempotent**: re-registering the
   same `subscriptionId` **replaces the previous handler**
   ([RemoteLocalHandlerRegistry.cs:41](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteLocalHandlerRegistry.cs)).
   A chain factored into a helper method and invoked twice therefore shares one source location → one id →
   the second registration **silently drops the first handler**. *Fix:* split **package identity**
   (source-location-stable, required for generator incrementality) from **registration/subscription
   identity** (unique per call/instance).

4. **CI red — investigated; the effect-drift hypothesis is refuted.** The Windows `Build` job's GameServer
   **smoke run** threw `RemoteServiceException: Internal error` from generated `BlinkBehindAsync`
   (`…/BlinkPluginPackage.g.cs:23`). Diagnosed by capturing the real install effect/capability sets
   in-process (bypassing the opaque wrapper): `BlinkBehindAsync`'s `[ServerExtension]` manifest declares
   effects `[Concurrency, Cpu, HostStateRead, HostStateWrite]` and caps `[runtime.async, …combat.threat,
   …entity.read.position, …monster.write.position]` — **exactly** what the host recomputes at install, so
   `RpcKernelPackageValidator`
   ([:84-88](../../../src/Hosting/DotBoxD.Plugins/Runtime/Rpc/RpcKernelPackageValidator.cs)) raises **no**
   `DBXK041`/`DBXK044`. The fingered `DotBoxDHandleModelFactory.CreateFromSend` change is on the
   `ctx.Messages.Send` event/hook path, **not** the `[ServerExtension]` path (`RpcKernelModelFactory` →
   `DotBoxDRpcJsonLowerer` → `AutoEffectNames`, never `CreateFromSend`); even on the Send path the added
   effects *match* the runtime `host.message.send` binding, so they align rather than drift. The smoke ran
   **green 5+ times** locally at the failing commit with SDK `10.0.204` (the CI-pinned version) and **does not
   reproduce**. It is a runtime IPC dispatch fault that never surfaces in-process — most consistent with a
   **Windows named-pipe / startup-timing flake** on the CI runner, not a code bug. *Action:* **re-run CI**;
   if it recurs, investigate the smoke's IPC/startup ordering (not effects). A deterministic in-process guard
   now pins the effect seam:
   [BlinkServerExtensionRegressionTests.cs](../../../samples/GameServer/Examples.GameServer.Plugin.Tests/BlinkServerExtensionRegressionTests.cs)
   (installs + invokes under Auto/Compiled/Interpreted; asserts the effect set, no `DBXK041`, result `== 3`).

### P2 — design hazards (fix before baselining)

5. **Generated-remote fallback recognition is name-based** with no ownership check (P2.4), and
   default-context inference is a whole-compilation scan that bails on a 2nd server (P2.5). See §3.3 for
   exact lines and fixes. Impact: breaks multi-server projects and any third-party fluent API whose members
   are named `Hooks`/`Subscriptions` or whose types end in `HookRegistry`/`SubscriptionRegistry`.

6. **Host-binding rule duplicated across the analyzer↔runtime trust seam** — single-source the rule, keep
   the check (§3.4). Scope is broader than `id` + `effects`: the `IsWriteMethod` name heuristic is duplicated
   ([lowerer:226](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs),
   [runtime:218-227](../../../src/Hosting/DotBoxD.Plugins/Runtime/Bindings/HostServiceBindingFactory.cs)).
   Also: capability metadata is read from **different declarations** on the two sides — the analyzer's
   auto-binding reads `[HostCapability]` off the **interface** method
   ([DotBoxDHostBindingExpressionLowerer.cs:163](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs),
   `HostCapability` helper at :195), while the runtime reads `[HostCapability]` off the **implementation**
   method and throws if absent
   ([HostServiceBindingExtensions.cs:52,55-56](../../../src/Hosting/DotBoxD.Plugins/Runtime/Bindings/HostServiceBindingExtensions.cs)).
   The GameServer abstractions carry `[HostCapability]` **without** `[HostBinding]` (the auto-binding path).
   **Resolved:** the **implementation** is authoritative; fall back to the **interface** when the
   implementation declares nothing. Apply that one precedence (impl → interface) identically on both sides,
   defined once in the shared rule (§3.4).

7. **Delete the back-compat surface; do not just hide it.** The `new`-shadowing `<TEvent>` shims
   ([HookPipeline.Default.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/HookPipeline.Default.cs))
   exist only to keep the un-parameterized form compiling alongside `<TEvent, TContext>` — a compatibility
   artifact this project does not want. **Remove the `<TEvent>` family entirely; keep one
   `<TEvent, TContext>` form.** Separately, the generator-only `UseGenerated*` / `UseProjecting*` plumbing
   ([RemoteHookPipeline.cs:36](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteHookPipeline.cs))
   should be `[EditorBrowsable(EditorBrowsableState.Never)]` or moved to an internal surface. Then
   regenerate the api-baseline in one breaking commit.

8. **Typed hook/subscription overload drift is a latent codegen build break, not polish.**
   `RemoteHookPipeline.Typed` exposes **12** `UseGeneratedLocalChain` overloads
   ([RemoteHookPipeline.Typed.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/Remote/RemoteHookPipeline.Typed.cs):77,82,94,102,112,118,131,140,153,159,172,181);
   `RemoteSubscriptionPipeline.Typed` exposes **10**
   ([RemoteSubscriptionPipeline.Typed.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Subscriptions/RemoteSubscriptionPipeline.Typed.cs):79,84,96,102,115,124,137,143,156,165)
   — **missing the two element-only no-decoder forms** present on the hook pipeline at :94
   (`Func<TEvent, ValueTask>`) and :102 (`Action<TEvent>`). The same stage-level gap exists between
   `RemoteHookStage.Typed` and `RemoteSubscriptionStage.Typed`. On `main` both sides had parity, so this is a
   regression. *Fix:* restore parity (collapse onto a `kind`-parameterized base), **or** prove the generator
   never emits the element-only subscription local-chain shape (gap unreachable) — and state which.

9. **`DBXK110` is stale and misleading.** The generator now lowers fluent `Run` chains, but the analyzer
   still **unconditionally** reports `DBXK110` (`Info`) on every `Run(lambda)` terminal: descriptor
   `RunNotLoweredRule` at [PluginAnalyzer.cs:36-44](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginAnalyzer.cs)
   (message: *"Run(lambda) is not yet lowered to verified IR and will throw at runtime"*), reported with no
   lowering check in `AnalyzeHookChainTerminal` at :82. The unshipped release note is doubly stale — it
   still describes `DBXK110` as *"InvokeKernel(lambda) chain is not yet lowered"*
   ([AnalyzerReleases.Unshipped.md:9](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/AnalyzerReleases.Unshipped.md)).
   So the rule **title** ("Run chain"), the rule **message** ("Run(lambda)"), and the **release note**
   ("InvokeKernel(lambda)") disagree with each other and with the shipped lowering. *Fix:* a `Run(lambda)`
   the generator lowered must not emit `DBXK110`; genuinely un-lowerable chains should surface the specific
   generator-side diagnostic (the `DBXK111`/`DBXK113`/`DBXK062` family, §3.2), not a blanket terminal flag.
   This is distinct from §3.2: `DBXK110` (`Run`) is **stale and should be removed/scoped**, whereas
   `DBXK111`/`DBXK113` are **valid but under-leveled**.

### P3 — polish

10. **Stale doc-term sweep** across `server-walkthrough.md`, `plugin-walkthrough.md`, and
    `kernel-binding-model.md`: replace `server.Events` → `server.Subscriptions`
    ([server-walkthrough.md:269](server-walkthrough.md)), and update `server.Kernels.Register`
    ([:263](server-walkthrough.md)) and any remaining `InvokeKernel` references to the current surface.
11. **Dead code** — `IPluginEventPipelineRegistry` has a single declaration repo-wide and no implementor or
    caller ([ServerContextFactory.cs:17](../../../src/Hosting/DotBoxD.Plugins/Runtime/ServerContextFactory.cs)).
    Delete.
12. **Sample teaches noise** — `(e, _) =>` for filters/projections that ignore `ctx`
    ([Program.cs:91](../../../samples/GameServer/Examples.GameServer.Plugin/Program.cs)); use `e =>` unless the
    body references `ctx`. Promote the rule ("arity names intent") into the §3.2 tier table.
13. **`RegisterLocal` has three authoring shapes** — value-only, `(e, ctx)`, and the legacy cancellation
    `(e, ctx, ct) => ValueTask<TResult>` — against the "exactly two" rule. **Drop the cancellation form**
    (`ctx.CancellationToken` is already exposed); no need to keep it for compatibility.

---

## Part 5 — Sequencing

**Step 1 — unblock merge; separate the surgical from the open-ended.**
- **1a.** Land the three localized correctness fixes: P1.1 (factory, fail-fast), P1.2 (priority — via the
  **registry-level sequence**, not the naive merge-sort), P1.3 (split package vs subscription identity).
  Each is a single-subsystem change.
- **1b.** **Diagnose P1.4 to a code path before estimating it.** If the root cause is the analyzer↔runtime
  effect drift (P2.6), the minimal unblock is a targeted effect-token fix; §3.4 is the durable fix. Do not
  let CI force the §3 redesign early.

**Step 2 — de-risk identity (§3.3).** Resolve the context from the receiver return-type argument (closes
P2.5) and gate `CandidateKind` on `[GeneratePluginServer]`-owned types (closes P2.4). The convention-named
context stays; only *identity resolution* changes.

**Step 3 — the larger moves, as their own PRs.** §3.1 (author-declared context surface — **only after** the
§3.1 remaining decisions are settled) and §3.4 (single-sourced host-capability rule). Not bolted onto #88.

> **Half-state note.** Merging #88 + Step 2 without Step 3 leaves the context still convention-named (its
> extension surface still undiscoverable) but with correct ownership-based identity. There is **no
> compatibility debt** — nothing ships to external users — so the only cost is the discoverability gap
> staying open until Step 3 lands. Acceptable as an interim state; just don't call the context "done" until
> the author-declared form (§3.1) replaces the convention partial.

**Step 4 — polish + surface shrink** (P2.7 + P3.10–13), ideally alongside the §3.4 duplication collapse so
the hook/subscription axis cannot drift again.

---

## Part 6 — Tests to add before accepting the direction

Acceptance gates, not afterthoughts. Existing homes: `ResultHookSlotTests`, `TypedServerContextTests`,
`HookPipelineFluentTests`.

- **Cross-context result priority (P1.2).** Register a priority-`0` result handler in an earlier-created
  context pipeline and a priority-`100` handler in a later one for the same event; assert the `100` handler
  wins.
- **Conflicting context factories (P1.1).** Two `On<E, Ctx>(factoryA)` / `On<E, Ctx>(factoryB)` calls — for
  **both** hooks and subscriptions — either throw or yield distinct pipelines; never silently share
  `factoryA`.
- **Reusable-helper composition (P1.3).** Invoke one `RunLocal` helper twice (two event sources); assert
  **both** native handlers remain registered and fire.
- **Foreign `.Hooks.On` negative analyzer fixture (P2.4).** A non-DotBoxD fluent API exposing
  `Hooks.On<T>()` / `Subscriptions.On<T>()` (and a type named `…HookRegistry`) is **not** intercepted.
- **Multi-server context ownership (P2.5).** Two `[GeneratePluginServer]` types in one compilation each
  resolve their own generated context for parameterless `On<TEvent>()`.
- **Host-binding descriptor parity (P2.6 / §3.4).** Analyzer-derived route + effects equal runtime-derived
  route + effects for the same `[DotBoxDService]` method (guards the `DBXK041` seam at compile time).
- **Typed hook/subscription overload parity (P2.8).** `RemoteHookPipeline.Typed` and
  `RemoteSubscriptionPipeline.Typed` (and the `…Stage.Typed` pair) expose the same `UseGeneratedLocalChain`
  set, including the two element-only forms.
- **`DBXK110` non-emission (P2.9).** A `Run(lambda)` site the generator lowers does **not** emit `DBXK110`;
  an un-lowerable chain emits the specific generator diagnostic instead.
- **Deterministic `BlinkBehindAsync` (P1.4) — added.**
  [BlinkServerExtensionRegressionTests.cs](../../../samples/GameServer/Examples.GameServer.Plugin.Tests/BlinkServerExtensionRegressionTests.cs)
  installs + invokes the `[ServerExtension]` in-process under Auto/Compiled/Interpreted, asserting effects
  `[Concurrency, Cpu, HostStateRead, HostStateWrite]`, no `DBXK041`, and result `== 3`. (Does not cover the
  IPC transport path — that is the smoke's job.)
- **`[ServerExtension]` graft from a real plugin assembly.** A plugin project that references a *prebuilt*
  SDK (no `[GeneratePluginServer]` of its own) authors a `[ServerExtension(typeof(IMonster))]` +
  `[ServerExtensionMethod]`, and it lowers, emits the grafted extension in the plugin's own namespace,
  installs under policy, and invokes — in-process **and** over IPC. (The single-assembly sample does not
  cover this.)
- **Grafted extension composes inside a chain.** A `[ServerExtension]` method called inside a lowered
  `.Run`/`.Where` lowers and runs server-side (no extra roundtrip); the same method called standalone goes
  over IPC. Both paths return the same result (proves Q4).
- **Context extension is locally discoverable.** Assert the author reaches the context's extension point
  from something they wrote — an author-declared context type attached via
  `[GeneratePluginServer(Context = typeof(...))]`, or a generated extension in the author's namespace — with
  no reference to a name-derived `{Root}Context` type.
- **Plugin-fluent docs smoke.** Add these design docs to a stale-term / compiled-snippet check so
  `server.Events`, `server.Kernels.Register`, and old `Invoke*` names cannot regress unnoticed.

---

## How this doc was reviewed

Cross-checked through independent review lenses — *Simple, Obvious, Discoverable, Consistent, Minimal,
Composable, Explicit, Stable, Testable, Boring* — plus correctness audits against head `41ec9172`. Where a
reviewer's proposed fix was itself wrong, the corrected version is what appears above — specifically:
the P1.2 "merge-sort by `(priority, order)`" fix (rejected: `_order` is per-`ResultHookSlot`, not global —
[ResultHookSlot.cs:25,235](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/ResultHookSlot.cs)), and the
§3.4 "remove the `DBXK041` drift class entirely" framing (rejected: `DBXK041`/`DBXK044` are the
trust-boundary cross-check — [PluginPreparedPackageValidator.cs:23-27](../../../src/Hosting/DotBoxD.Plugins/Runtime/Validation/PluginPreparedPackageValidator.cs)).
