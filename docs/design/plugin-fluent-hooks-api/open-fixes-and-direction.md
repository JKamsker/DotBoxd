# Plugin context & hooks: open fixes and design direction

Companion to [server-walkthrough.md](server-walkthrough.md),
[plugin-walkthrough.md](plugin-walkthrough.md),
[../remote-plugin-server-builder/interface-driven-plugin-server.md](../remote-plugin-server-builder/interface-driven-plugin-server.md),
and [kernel-binding-model.md](kernel-binding-model.md).

These companion docs are historical and contain known stale API examples; see P3.15 before using them as
implementation guidance.

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

1. **Correctness/security — blocks merge.** PR #88 has localized hook/runtime correctness bugs (P1.1
   factory/keying, P1.2 result-hook ordering, P1.3 local-helper identity), one red CI smoke (P1.4), and
   trust-boundary/lifecycle gaps that must not be buried under the context redesign (P1.5–P1.8). None depends
   on the context's *shape*; each gates merge on its own.

2. **Shape — a separate, larger thesis.** The generated plugin context
   ([PluginServerContextSurfaceEmitter.cs:16](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/PluginServer/PluginServerContextSurfaceEmitter.cs))
   is the **only generated type the author extends by hand at a name they must already know** —
   `{Root}Context`, derived from the server class name, with nothing they wrote pointing to it
   ([GamePluginContext.cs:3-10](../../../samples/GameServer/Examples.GameServer.Plugin/GamePluginContext.cs)).
   The fix is to make the context **author-declared** (you name the type; you extend the type you named —
   §3.1), removing the convention-named partial. One hard constraint: `[KernelMethod]` cannot live on an
   interface (its body must be inlined), so the declared context is a `partial` **class** (§3.1). Lands as
   its own PR(s) after §3.1's contract details are settled.

One-line direction: **fix P1.1–P1.8 first; make the context surface declarable only after §3.1's contract
details are explicit; fix chain/context identity with semantic generated metadata (§3.3); single-source the
host-capability derivation rule without removing the host's independent install-time recomputation (§3.4).**
Throughout, the **audience split** is load-bearing: the server author ships an SDK; the plugin dev consumes
it and **safely extends** it with `[ServerExtension]` — see
[The two audiences](#the-two-audiences-and-the-plugin-dev-extension-surface).

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
| static `[KernelMethod]` | a pure scalar **inline helper** (predicate / derived value) used inside a lowered `Where`/`Select`/`Run` | scalar-only; no host-service/context access in the current plan |

`[ServerExtension]` is the primary lever. A grafted method **composes both ways** — callable inside a lowered
hook chain (it lowers and runs server-side, no extra roundtrip) **and** standalone over IPC (Part 2). Today
its trailing context parameter is raw `HookContext`; the §3.1 end state is the generated SDK context
(`GameContext`) with `HookContext` still accepted as an escape hatch via `GameContext.Raw`. This is a real
codegen task, not just doc wording: current discovery accepts `HookContext`, so generated-context
server-extension parameters need explicit lowering/resolution support and tests. The reference example is
`BlinkKernel`
([Kernels/BlinkKernel.cs](../../../samples/GameServer/Examples.GameServer.Plugin/Kernels/BlinkKernel.cs)):
`[ServerExtension(typeof(IMonster))]` injects the addressed monster + the root world, and
`[ServerExtensionMethod] BlinkBehindAsync(string playerId, HookContext ctx)` currently does a root-world read + a
scoped read, computes, then performs a host write (`TeleportToAsync`). Its capabilities —
`game.world.combat.threat`, `game.world.entity.read.position`, `game.world.monster.write.position` — are
exactly the host bindings it touches, gated at install, so the plugin extends the API **only within its
granted authority**. The generated extension lands in the author's namespace
(`…Kernels.BlinkKernelDirectServerExtensionClientExtensions.BlinkBehindAsync`), discoverable on any
`IMonster` with no convention name to know. `MonsterKillerKernel` is the collection variant (grafted onto a
control for batch/list aggregation). The generated graft surface also needs a same-namespace duplicate
signature diagnostic: two kernels must not silently emit indistinguishable extension methods on the same
receiver.

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
[GeneratePluginServer(Context = typeof(GameContext))]   // required; explicit, greppable
public partial class GamePluginServer : IGameWorldAccess;

public sealed partial class GameContext { /* server-author SDK members */ }
```

The generator augments `GameContext` (the ambient-`HookContext` wrapping + conveniences) as a partial of
*your* type, and any generated extension methods land **in your namespace** so IntelliSense surfaces them in
place. The convention-named `{Root}Context` partial is **removed**, not kept as a default.

**Decisions and implementation details (small but blocking):**

- **Ownership: decided — option (a).** Three distinct senses, kept separate:
  - **Authority** (what a capability-bearing call may do) is **host-owned, always** — enforced by the
    verifier (unknown-binding rejection + `DBXK041`/`DBXK044`), independent of where anything is declared.
    Not a design choice.
  - **Declaration:** the context is **server-authored and ships in the SDK** — it carries the re-exposed
    server-owned `[DotBoxDService]` services (`ctx.World.Damage.GetAdjustment(id)`, auto-lowering to a host
    binding) plus any server-authored helpers, and **never** plugin-declared `[HostBinding]` members.
    (`ctx.Messages.Send(...)` already works this way — a re-exposed `IPluginMessageSink`, not a
    plugin-declared binding.) A **plugin dev's** own `[KernelMethod]` helpers are **static methods in the
    plugin's own assembly** (inlined into their chains), **not** members on the context — a context compiled
    in the SDK cannot be extended by a `partial` across assemblies. Whole new operations are
    `[ServerExtension]`, not context members (see
    [The two audiences](#the-two-audiences-and-the-plugin-dev-extension-surface)).
  - **Host-binding metadata** (P2.6): the analyzer cannot inspect the host's concrete implementation from a
    plugin assembly, and factory-returned handle implementations are not known when handle descriptors are
    created. Therefore the analyzer-visible SDK contract remains authoritative for plugin-visible
    auto-bindings and handles unless a future generated descriptor projection makes implementation metadata
    visible to the analyzer. The runtime may prefer concrete implementation metadata only where it has both
    declarations, but the shared rule must define exactly which declaration is authoritative per binding kind.
- **Attribute contract: decided.** `Context = typeof(TContext)` is **required** once the convention partial is
  removed. `TContext` must be a non-generic, non-nested `partial class` declared in the same compilation as
  the `[GeneratePluginServer]` class; it may live in a different namespace. The generator carries the
  context's fully qualified name, namespace, declared accessibility, and modifiers, emits its partial
  augmentation in the context namespace, and uses the FQN in generated `On<T>()` signatures. The context must
  be at least as visible as the generated facade/registry surface that exposes it (for a public facade, a
  public context). Lowering the generated surface to match a less-visible context is rejected as surprising;
  invalid visibility/shape produces a build diagnostic before generation. No fallback to `{Root}Context` once
  this mode is enabled.
- **Shape: a `partial` class, not an interface.** Interface members have no body to inline for
  `[KernelMethod]` (the limit above). `sealed` is allowed and recommended for SDK contexts unless a concrete
  extension scenario needs inheritance; `partial` is required so the generator can augment the type the
  server author named.
- **Lifetime/construction: decided.** The generator emits `FromHookContext(HookContext raw)` and the wrapping
  constructor into the author's partial **by default**, built per publish as today
  ([HookRegistry.cs:95](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.cs)). Custom construction
  uses an explicit attribute string, e.g. `ContextFactory = nameof(GameContext.Create)`, resolved on the
  declared context type. The factory must be a static method visible to generated code with signature
  `TContext Factory(HookContext raw)`; a missing, overloaded, non-static, or wrong-return factory is a build
  diagnostic.
- **Service selectors:** `ctx.World`/control members are lowerable SDK service selectors, not live native
  proxies for arbitrary plugin code. They are valid in server-side lowered bodies; using them from `[Local]`
  context members or native terminals is a diagnostic. A future IPC client surface must be explicitly named
  and separately reviewed, never reached through an accidental host-binding shortcut.

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

A callable helper/service used by a chain runs in one of three locations. The table has four authoring tiers
across those locations. The current signal — an attribute, or its **absence** — is too implicit; **"no marker
⇒ native" is a footgun.**

| Tier | Marker | Runs where | Body may reference | Author rule |
|---|---|---|---|---|
| SDK context helper | `[KernelMethod]` on the server-authored context | server-side sandbox (verified IR) | scalars; other `[KernelMethod]` members; re-exposed host-service calls; **no** native services | "pure computation over event fields" |
| Plugin static helper | static `[KernelMethod]` in the plugin assembly | server-side sandbox (verified IR) | scalar parameters/returns only in the current plan; **no** context/service parameter and no native services | "plugin-local pure helper, not a context member" |
| Host capability | a re-exposed `[DotBoxDService]` member (`ctx.World.X()`; auto-lowers) — **not** a `[HostBinding]` on the context | server-side host | the host call, gated by its `[HostCapability]` | "reads/writes host/game state" |
| Native SDK helper | **`[Local]`** (decided; today it is the absence of a marker) | server-authored SDK/native side, post-IPC | SDK-provided in-process helper code; **no** host-service selectors | "server SDK native convenience, not plugin extension" |

This table is about callable helpers/services used inside a chain. Rows 1, 3, and 4 are context or
context-backed members; row 2 is a plugin-owned static helper. A whole grafted operation is the separate
(and primary) plugin-extension mechanism — `[ServerExtension]`/`[ServerExtensionMethod]`, see
[The two audiences](#the-two-audiences-and-the-plugin-dev-extension-surface). `[KernelMethod]` here is only
the pure inline helper, not the main way to extend the API.

Two precise corrections:

- **The verbs are not interchangeable.** `Run`/`Register` lower to verified IR (sandbox subset only);
  `RunLocal`/`RegisterLocal` run arbitrary native code. A `RunLocal` body that calls a plugin service does
  **not** become a valid `Run` by dropping the suffix — it fails to lower. Do **not** claim "the same
  expression, the suffix chooses where it runs."
- **Native is opt-in via `[Local]` (decided — option A).** A native (in-process) context member carries
  `[Local]`; execution site is never inferred from a *missing* attribute. The analyzer raises a **build
  error** if a `[Local]` member is used in a lowered stage (`Where`/`Select`/`Run`/`Register`) or a lowered
  `[ServerExtensionMethod]`/RPC body — a new
  diagnostic alongside the `DBXK111`/`DBXK113`/`DBXK062` family. (Rejected: splitting the context into a
  *lowerable facet* + a *native facet* — more types, less minimal.) The native terminal is the
  trust-boundary exit, so the generator must still route tiers by **owned symbol identity** (§3.3), never by
  string name (P2.4), so a typo or a foreign API cannot route a body to the wrong tier.
- **`[Local]` contract.** Add `LocalAttribute` in `DotBoxD.Abstractions`, `AttributeTargets.Method |
  AttributeTargets.Property`, non-inherited. It is valid only on instance members of the declared server SDK
  context type. Applying it to static helpers, arbitrary plugin classes, services, or members referenced from
  lowered stages is a diagnostic. Properties are allowed only for local/native use; they are not lowerable
  host-service selectors. This is **not** the plugin developer's native-extension hook under the SDK split:
  plugin-owned native code remains `RunLocal`/`RegisterLocal` callbacks (and standalone IPC clients), because
  a plugin assembly cannot add partial context members to the prebuilt server SDK. A future plugin-owned
  native helper contract would need its own marker, callback identity model, and tests; do not imply it here.
- **Static `[KernelMethod]` scope.** Keep plugin static helpers scalar-only unless a later PR explicitly adds
  context-parameter rebinding for static helpers. That later work would need diagnostics for context type /
  accessibility, lowerable service-selector use, and rejection of `[Local]`/native escapes through the helper.
- **Prebuilt SDK helper bodies.** Server-authored SDK context `[KernelMethod]` helpers cannot rely only on
  Roslyn syntax inlining once the context ships as a compiled SDK: metadata-only methods have no
  `DeclaringSyntaxReferences`. The §3.1 implementation must emit analyzer-visible helper descriptors into
  the SDK package for context helpers, and the plugin-side analyzer must consume those descriptors when the
  method body is metadata-only. Use generated SDK metadata, not a loose sidecar: add this public Abstractions
  carrier:
  `[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)] public sealed class
  GeneratedKernelMethodDescriptorAttribute : Attribute`, with constructor
  `(int version, Type contextType, string methodMetadataName, string normalizedSignature,
  string descriptorHash, string descriptorPayload)` and read-only properties with the same names/types
  (`Version`, `ContextType`, `MethodMetadataName`, `NormalizedSignature`, `DescriptorHash`,
  `DescriptorPayload`). `DescriptorPayload` is canonical UTF-8 JSON, and `DescriptorHash` is lowercase hex
  SHA-256 over that canonical payload. `AllowMultiple = true` is required because one SDK assembly can carry
  many context helpers. The attribute must be emitted by the same assembly identity that contains the context
  type and method it describes.
  The payload carries verified helper IR plus return type/allocation shape, required capabilities, effects,
  context type, and method signature identity. The consuming analyzer accepts the descriptor only when it binds
  to a `[KernelMethod]` on the validated server SDK context with the exact signature/assembly identity, then
  revalidates the IR under the same sandbox rules. It recomputes required capabilities/effects from the
  revalidated IR, rejects any mismatch with descriptor metadata, and merges the recomputed transitive
  capabilities/effects into the caller's manifest. Same-compilation helpers may keep the syntax inliner, but
  descriptor generation still needs `this.World...` / implicit `World...` receiver rebinding so context helper
  bodies lower the same way they will after packaging. A metadata-only `[KernelMethod]` without a matching
  descriptor is a diagnostic, not a silent native call.
- **Context `[HostBinding]` is deleted, not renamed.** The current convention context documentation and
  member-chain lowerer still allow plugin-owned context `[HostBinding]` members. The §3.1 redesign must add a
  build diagnostic or remove that lowering path entirely, and tests must prove context `[HostBinding]`
  members are rejected.

**Failure mode (exact).** A non-lowerable member used in a lowered stage compiles, then throws **`DBXK062`**
via `SandboxValidationException` **when the authoring chain is executed/registered, before event dispatch** —
`HookStage.NotLowered()` at
[HookStage.cs:119](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/HookStage.cs) and
`ResultNotLowered()` at :126 — not during package install. Build-time author detection exists but is
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
  loses default-context inference. *Fix:* keep two semantic paths, no scan. For a **prebuilt/cross-assembly
  SDK**, the generated `On<TEvent>()` return type is visible, so read `{Context}` from
  `RemoteHookPipeline<TEvent, TContext>` / `RemoteSubscriptionPipeline<TEvent, TContext>`. For a
  **same-compilation generated facade**, the generator cannot see its own generated `On<TEvent>()` members
  yet. Step 2 is dual-mode: if the new `Context` attribute is already present, derive the context from it;
  otherwise derive the current convention context from the receiver server symbol without doing a
  whole-compilation scan. Once §3.1 lands, the convention branch is deleted. Both paths must carry a context
  FQN internally, not a guessed unqualified name.
- **P2.4 — is this even a DotBoxD chain.** `CandidateKind`'s fast path
  ([GeneratedRemoteHookChainFallback.cs:23](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/Support/GeneratedRemoteHookChainFallback.cs))
  switches purely on the receiver member's identifier text `Hooks`/`Subscriptions` (:31), with **no**
  semantic check, and the "semantic" fallback `RegistryKind` (:42) is itself only a
  `Name.EndsWith("HookRegistry")`/`"SubscriptionRegistry"` suffix match (:45-46). A foreign fluent API named
  `Hooks`/`Subscriptions` is mis-claimed. *Fix:* gate **both** paths semantically. Direct
  `server.Hooks.On<T>()` may resolve from the server receiver symbol and its `[GeneratePluginServer]`
  attribute. Same-compilation aliases need a small semantic/syntax alias path for locals assigned from
  `server.Hooks`/`server.Subscriptions` before generated types exist. Prebuilt SDKs and aliased/stored
  generated registry values need durable generated metadata: add a public analyzer metadata attribute in
  `DotBoxD.Abstractions`, e.g.
  `[GeneratedPluginServerRegistry(GeneratedPluginServerRegistryKind.Hook, typeof(GamePluginServer), typeof(GameContext))]`,
  emitted on generated hook/subscription registry types and included in the API baseline. The public contract
  is concrete before implementation: `AttributeUsage(AttributeTargets.Class, Inherited = false,
  AllowMultiple = false)`, constructor `(GeneratedPluginServerRegistryKind kind, Type serverType,
  Type contextType)`, read-only `Kind`, `ServerType`, and `ContextType` properties, and enum values `Hook`
  and `Subscription`. The marker is public metadata, so do not claim unobservable generator provenance.
  Treat it as an explicit semantic opt-in that is accepted only with ownership/shape validation: `ServerType`
  must be a generated/`[GeneratePluginServer]` facade, its `Hooks`/`Subscriptions` property for `Kind` must
  return the marked registry type, and `ContextType` must match the generated registry's pipeline return
  shape. Analyzer tests must ignore or diagnose inherited, duplicated, malformed, or user-authored lookalike
  markers that lack this ownership proof. Use an enum for the registry kind and `Type` arguments for
  owner/context so refactors keep semantic identity. `CandidateKind` checks that marker (or the
  same-compilation receiver/alias path), never member names or suffixes.

### 3.4 Single-source the host-capability rule — without weakening the check

"§3.4" means **one server-owned definition, two projections**, **not** "one descriptor object consumed by
both." A shared runtime object is not buildable: the analyzer is `netstandard2.0` with zero `ProjectReference`
(packed to `analyzers/dotnet/cs`) and operates on `IMethodSymbol`; the runtime is `net10.0` and operates on
`System.Reflection.MethodInfo`. Therefore:

- Put the binding-id formula and the effect/allocation/classification rules in **one dependency-free source
  file**, linked into both projects via `<Compile Include Link>`, and have both `HostBindingRoute`
  implementations and both `ReturnAllocates`/`IsWriteMethod` implementations call it.
- Keep the linked core small and primitive: stable binding id, explicit effect tokens, required capability
  token, async flag, and primitive parameter/return shape identifiers. Runtime-only concepts (`SandboxType`,
  `SandboxEffect`, `BindingCostModel`, `AuditLevel`, `BindingSafety`) stay in runtime adapters; analyzer
  adapters map the same primitive core into manifest strings. The rule is shared; the descriptor objects are
  not.
- Replace the **method-name-prefix heuristic** `IsWriteMethod` (names starting `Kill/Set/Update/Delete/Add/
  Remove/Move/Teleport` ⇒ `HostStateWrite`, duplicated at
  [DotBoxDHostBindingExpressionLowerer.cs:226](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs)
  and [HostServiceBindingFactory.cs:218-227](../../../src/Hosting/DotBoxD.Plugins/Runtime/Bindings/HostServiceBindingFactory.cs))
  with an **explicit effect declaration**, so effects are no longer inferred from method names on two sides
  (a method named `Patch` or `Spawn` is silently read-only today on both).
- Concrete authoring shape: add a dependency-free `[Flags] HostBindingEffect` enum in `DotBoxD.Abstractions`
  with `None`, `HostStateRead`, `HostStateWrite`, and `Allocates`, and make host-capability declarations
  explicit, e.g. `[HostCapability("game.world.monster.write.position", HostBindingEffect.HostStateWrite)]`.
  `Concurrency` and the `runtime.async` capability are derived from async/Task-returning shape; `Cpu` remains
  module-execution overhead; audit/cost/safety stay runtime descriptor metadata. A declared flag that
  conflicts with signature-derived facts is a diagnostic, not a silent override. Missing effect metadata on an
  auto-bound `[DotBoxDService]` method is a build/runtime registration diagnostic, not a fallback to
  method-name inference.
- Define capability metadata precedence per binding kind in the shared core. For plugin-visible SDK
  contracts and handle methods, interface metadata is analyzer-visible and must remain authoritative unless
  the SDK emits a generated descriptor projection. For runtime services where a concrete implementation
  method is known, implementation metadata may override interface metadata, but only if the analyzer receives
  the same server-owned projection. Do **not** claim "impl → interface on both sides" without that projection.
- **Keep** the host's install-time recomputation and `DBXK041`/`DBXK044` (Part 2). One server-owned
  definition makes the check **un-driftable**; it does not remove it.

---

## Part 4 — Open fixes (review backlog)

Every item verified against head `41ec9172`.

### P1 — blockers (correctness/security; independent of the §3 redesign)

1. **Factory collapse.** `On<TEvent, TContext>(createContext)` validates `createContext`
   ([HookRegistry.cs:82](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.cs)), keys the pipeline
   cache on `PipelineKey(typeof(TEvent), typeof(TContext))` (:86), and on a cache hit (:87) returns the
   existing pipeline — **discarding the just-passed `createContext`**. Two call sites with the same
   `(TEvent, TContext)` but different factories silently share the first factory. *Fix:* do **not** key on
   delegate identity — keep one pipeline per `(event, context)` and **throw on a conflicting factory** while
   preserving idempotent reuse of the same method group/factory. Also fix the `HookContext` special case:
   `On<E, HookContext>(...)` and `On<E>()` share the same key but may store incompatible pipeline runtime
   types depending on call order, producing an `InvalidCastException` instead of a deterministic result. The
   fix must canonicalize the default/explicit `HookContext` path. Same patterns exist in
   [SubscriptionRegistry.cs:69,73-74](../../../src/Hosting/DotBoxD.Plugins/Runtime/Subscriptions/SubscriptionRegistry.cs).

2. **Result-hook priority is no longer global** once an event has >1 context pipeline. `FireManyAsync`
   ([HookRegistry.Pipelines.cs:59](../../../src/Hosting/DotBoxD.Plugins/Runtime/HookRegistry.Pipelines.cs))
   iterates pipelines in `Dictionary` order and returns the **first non-null** result (:70-72); priority is
   sorted **only within one slot** — `_order` is an instance field
   ([ResultHookSlot.cs:25](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/ResultHookSlot.cs)),
   incremented per-slot in `Add` (:235), and the sort is per-slot (:240). **So the naive fix fails:**
   merge-sorting by `(priority, order)` cannot order across pipelines because `order` is not comparable
   across slots. *Fix:* introduce an **event-level result table** (or equivalent registry-owned ordering
   model) so priority and equal-priority install order are total across context pipelines. Result dispatch
   options remain part of dispatch ownership: when callers use no explicit options, the winning handler's
   owning pipeline supplies its configured remote-timeout/default-result behavior; an explicit
   `FireAsync(..., options)` overrides stored pipeline options for that dispatch. Context construction also
   remains part of handler ownership: every result handler is invoked with its owning pipeline's context
   factory, never the first pipeline's or the event-table builder's context. (On `main`, `_pipelines` was
   keyed by event type alone —
   one pipeline per event — so `FireManyAsync` did not exist; this PR rekeys to `(EventType, ContextType)`
   and adds the multi-pipeline walk, introducing the regression.)

3. **Reusable `RunLocal`/`RegisterLocal` helpers do not compose.** Chain identity is the **call-site source
   location**: `HookChainIdentity.Compute` returns `FNV1a(path + ":" + SpanStart)`
   ([HookChainIdentity.cs:14-19](../../../src/CodeGeneration/DotBoxD.Plugins.Analyzer/Analysis/HookChains/HookChainIdentity.cs)),
   and that id is reused as the **plugin/package id** and callback **subscription id**, whose registry is
   **idempotent**: re-registering the same `subscriptionId` **replaces the previous handler**
   ([RemoteLocalHandlerRegistry.cs:41](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteLocalHandlerRegistry.cs)).
   A chain factored into a helper method and invoked twice therefore shares one source location → one id →
   the second registration **silently drops the first handler**. Server-side install has the same identity
   problem: installing the second package with the same `PluginId` replaces the first kernel and removes its
   hook/subscription references. *Fix:* define **three** identities, not one replacement id:
   **plugin/package principal** (stable manifest/module identity for policy, allowlist, audit, server
   extension lookup, and replacement authority), **install/kernel instance identity** (lifecycle, rollback,
   hot-replace ownership), and **opaque callback subscription identity** (unique per `RunLocal`/
   `RegisterLocal` registration and returned from install for callback routing). Source-location identity may
   still feed generator incrementality and deterministic package metadata, but it is not the callback id and
   must not overwrite the stable plugin principal. Setup-time recording/replay is part of the fix, and
   local-terminal installation must be atomic with handler registration: either preallocate the callback id
   and register the client-side handler before server routing is activated, or use a two-phase pending install
   with client acknowledgement and rollback on handler-registration failure. The server must not expose an
   active route whose callback id is absent from `RemoteLocalHandlerRegistry`. Removal paths must use
   install/kernel instance identity so disposing or replacing one instance of a same-principal helper removes
   only that instance's hook/subscription/result-local callbacks and indexed registrations.

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
   [BlinkServerExtensionRegressionTests.cs](../../../samples/GameServer/Examples.GameServer.Plugin.Tests/Regression/BlinkServerExtensionRegressionTests.cs)
   (installs + invokes under Auto/Compiled/Interpreted; asserts the effect set, no `DBXK041`, result `== 3`).

5. **Index coverage is trusted across the manifest boundary.** `IndexCoversPredicate` is shape-validated from
   the package manifest, then the event index can skip `ShouldHandleAsync` when the manifest claims full
   coverage. A tampered package can claim a partial index fully covers the predicate and bypass verified IR.
   *Fix:* recompute index predicates/coverage from verified function bodies or install-path-owned expected
   metadata that is generated and cross-checked outside the package's mutable manifest/module metadata; skip
   `ShouldHandleAsync` only when coverage was recomputed/trusted for **this install**. Without provenance,
   direct in-memory `PluginPackage` installs are just as untrusted as JSON imports. Add tampered-manifest
   regression tests for both exported JSON import and direct in-memory packages. If a new trusted
   expected-metadata source is added, forge/mutate it too and prove it is authenticated or cross-checked.

6. **Capability-request policy mixing weakens the install gate.** The plan describes `DBXK044` as parity for
   host-derived entrypoint capabilities, but current required-capability helpers also include plugin-declared
   module capability requests; the GameServer sample then grants that aggregate set automatically. *Fix:*
   split **host-derived required capabilities** from **plugin-requested capabilities**. `DBXK044` compares the
   host-derived set; plugin requests require an independent server allowlist/tenant decision before policy
   grants. This is a generator/model split too: stop mirroring host-derived `RequiredCapabilities` into module
   `CapabilityRequests`; generated hook/chain modules should have empty requests unless the plugin explicitly
   declares a separate request.

7. **Native-terminal routing is manifest-authoritative.** `RunLocal`/`RegisterLocal` are the trust-boundary
   exit, but manifest flags such as `LocalTerminal`, `ProjectedType`, `ResultType`, and `ResultLocalTerminal`
   can route an ordinary package into plugin-process native callbacks. Whole-event `RunLocal` is especially
   sensitive because its verified IR shape is the same unit-returning handle shape as an ordinary `Run`.
   *Fix:* use install-path-owned expectations from generated registration/interceptors (or host-authenticated
   metadata cross-checked outside the package's mutable manifest/module metadata) for terminal kind,
   projection type, result type, and result-localness before routing. Add tamper tests for all four fields and
   for any new trusted metadata source.

8. **Indexed subscriptions are not unregistered on disconnect/reinstall.** Indexed subscriptions can be
   registered into the event index, but normal session/kernel cleanup removes only hook/subscription registry
   references. Stale indexed kernels can survive disconnect or replacement. *Fix:* wire indexed registrations
   into the same kernel/session removal path (or make index registrations disposable and owned by the
   installed kernel), then add a reinstall/dispose regression that proves the old indexed handler is gone.

### P2 — design hazards (fix before baselining)

5. **Generated-remote fallback recognition is name-based** with no ownership check (P2.4), and
   default-context inference is a whole-compilation scan that bails on a 2nd server (P2.5). See §3.3 for
   exact lines and fixes. Impact: breaks multi-server projects, aliased generated registries
   (`var hooks = server.Hooks; hooks.On<T>()`), cross-assembly SDKs, and any third-party fluent API whose
   members are named `Hooks`/`Subscriptions` or whose types end in `HookRegistry`/`SubscriptionRegistry`.

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
   **Correction:** a blanket implementation-first rule is not currently implementable on both sides. The
   analyzer sees the SDK/interface symbol, not the host implementation; handle bindings are also created from
   handle interface methods. §3.4 must define an analyzer-visible, server-owned metadata source per binding
   kind before implementation metadata can override interface metadata.

7. **Delete the runtime back-compat surface; do not just hide it.** The `new`-shadowing `<TEvent>` shims
   ([HookPipeline.Default.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/HookPipeline.Default.cs))
   exist only to keep the un-parameterized form compiling alongside `<TEvent, TContext>` — a compatibility
   artifact this project does not want. **Remove the runtime compatibility `<TEvent>` family entirely; keep
   one runtime `<TEvent, TContext>` form.** This does **not** delete the generated ergonomic facade
   `server.Hooks.On<TEvent>()` / `server.Subscriptions.On<TEvent>()`; those parameterless generated methods
   remain and return the server-owned context type. Separately, the generator-only `UseGenerated*` /
   `UseProjecting*` plumbing
   ([RemoteHookPipeline.cs:36](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/RemoteHookPipeline.cs))
   must be classified deliberately: `[EditorBrowsable(EditorBrowsableState.Never)]` hides IntelliSense but
   does **not** shrink the public API or remove baseline entries. Either keep it public as an intentional
   generator contract and baseline it, or redesign generated calls so the implementation can become
   non-public. Then regenerate all affected api-baselines in one breaking commit.

8. **Typed hook/subscription overload drift is a latent codegen build break, not polish.**
   `RemoteHookPipeline.Typed` exposes **12** `UseGeneratedLocalChain` overloads
   ([RemoteHookPipeline.Typed.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/Remote/RemoteHookPipeline.Typed.cs):77,82,94,102,112,118,131,140,153,159,172,181);
   `RemoteSubscriptionPipeline.Typed` exposes **10**
   ([RemoteSubscriptionPipeline.Typed.cs](../../../src/Hosting/DotBoxD.Plugins/Runtime/Subscriptions/RemoteSubscriptionPipeline.Typed.cs):79,84,96,102,115,124,137,143,156,165)
   — **missing only the two element-only no-decoder forms** present on the hook pipeline at :94
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
   `DBXK111`/`DBXK113` are **valid but under-leveled**. Because `DBXK110` comes from a separate
   `DiagnosticAnalyzer`, a generator-only test is insufficient: remove or scope the analyzer descriptor,
   update analyzer baselines/release notes, then add combined analyzer+generator tests. Also add a precise
   generator diagnostic for recognized but unlowerable `Run(lambda)` chains; removing blanket `DBXK110`
   must not leave those chains silent.

10. **Subscription cancellation policy is undefined and currently noisy.** `SubscriptionPipeline.Publish`
    creates a canceled `HookContext` but still queues work, and `SubscriptionDelivery` reports
    `OperationCanceledException` from caller-token cancellation as a plugin fault. *Fix:* define cancellation
    semantics explicitly. At minimum, pre-canceled publish should not run local handlers, and caller-token
    cancellation should not be reported as a handler/filter fault. Apply the same policy to indexed
    subscription dispatch; the index path must not silently catch and drop cancellation/faults under a
    different contract.

11. **Generated graft collision handling is missing.** `[ServerExtension]` discoverability depends on
    generated extension methods in the plugin author's namespace, but two kernels can target the same
    receiver with the same extension signature. Existing checks cover conflicts with receiver members, not
    duplicate generated extension signatures. *Fix:* add a diagnostic for duplicate grafted method
    name/signature/receiver/namespace combinations.

12. **Generated-context server-extension parameters are promised but not implemented.** The design target is
    `[ServerExtensionMethod]` with a trailing `GameContext ctx` parameter and raw `HookContext` as an escape
    hatch, while current
    discovery accepts raw `HookContext`. *Fix:* implement generated-context resolution/lowering for server
    extensions and test it cross-assembly; raw `HookContext` remains the escape hatch.

13. **Server-extension receiver authority is not one contract.** The design calls `[ServerExtension(typeof(T))]`
    a graft onto a server-owned type, while current generation can accept any receiver shape exposing
    extension-client access, and receiver-id injection is derived differently on client and package sides.
    *Fix:* make receiver ownership and receiver-id injection one server-owned contract. For the safe extension
    surface, reject plugin-owned graft receivers; a separate unscoped plugin-owned extension model can be
    designed later under a different name. Derive client receiver-id argument shape from the same server-owned
    graft metadata used to build the package.

14. **Post-install GameServer wiring can leave installed-but-unwired kernels.** The IPC control service
    installs a package, then wires hooks/subscriptions/results/index routing afterward. If wiring throws, the
    session/server can retain an installed kernel that is not reachable through the intended routing path.
    ([GamePluginControlService.cs:76](../../../samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginControlService.cs),
    [:90](../../../samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginControlService.cs); failure
    examples in [GamePluginKernelWiring.cs:136](../../../samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginKernelWiring.cs)
    and [:150](../../../samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginKernelWiring.cs)).
    *Fix:* validate routing before install or roll back the install on wiring failure. Add a concrete
    supported-event tamper regression that proves no stale kernel remains; use a real throwing wiring path
    such as a local terminal / `RegisterLocal` package without callback transport, not index metadata that
    merely falls back to broad subscription dispatch.

### P3 — polish

15. **Stale doc-term sweep** across `server-walkthrough.md`, `plugin-walkthrough.md`, and
    `kernel-binding-model.md`: replace stale `server.Events.On` / fire-and-forget mirror prose with
    `server.Subscriptions`; separately update event-adapter registry examples to the current
    `server.Events.Resolve<TEvent>()` surface. Also update `server.Kernels.Register`, `server.Kernels.Get`,
    `SetValuesAsync`, `InvokeKernel`, and `InvokeLocal` references to the current surface, and fix
    moved-source links under old `../../../src/DotBoxD.*` paths.
    Known clusters: `server-walkthrough.md` intro diagram, context `[HostBinding]` example around 209-230,
    and lines around 263/269; `plugin-walkthrough.md` intro diagram and hook examples around
    171-202/235/302/318-319 plus live-settings examples around 148-149/341-342; `kernel-binding-model.md`
    opening table, links around 20/48/175/176/190, and examples around 147/181-199/214-226; and
    `interface-driven-plugin-server.md` capability/effect sections around 124-147 and 242-265.
16. **`[KernelMethod]` public docs drift.** The public XML docs still teach that `[KernelMethod]` must be
    static, while §3.1 requires server-context instance helpers. Update the docs, generated context XML docs,
    and user-facing diagnostics that still teach "plugin-owned context" / context `[HostBinding]` guidance
    when the context design lands.
17. **Dead code** — `IPluginEventPipelineRegistry` has a single declaration repo-wide and no implementor or
    caller ([ServerContextFactory.cs:17](../../../src/Hosting/DotBoxD.Plugins/Runtime/ServerContextFactory.cs)).
    Delete.
18. **Sample teaches noise** — `(e, _) =>` for filters/projections that ignore `ctx`
    ([Program.cs:91](../../../samples/GameServer/Examples.GameServer.Plugin/Program.cs)); use `e =>` unless the
    body references `ctx`. Promote the rule ("arity names intent") into the §3.2 tier table.
19. **`RegisterLocal` has three authoring shapes** — value-only, `(e, ctx)`, and the legacy cancellation
    `(e, ctx, ct) => ValueTask<TResult>` — against the "exactly two" rule. **Drop the cancellation form**
    (`ctx.CancellationToken` is already exposed); no need to keep it for compatibility.

---

## Part 5 — Sequencing

**Step 1 — unblock merge; separate the surgical from the open-ended.**
- **1a.** Land the three localized correctness fixes: P1.1 (factory, fail-fast), P1.2 (priority — via the
  **event-level ordering model**, not the naive merge-sort), and P1.3 (separate stable plugin/package
  principal, install/kernel instance identity, and opaque callback subscription identity; keep
  source-location ids only for generator incrementality/deterministic metadata). These are localized but not
  all single-file.
- **1b.** Land the trust-boundary/lifecycle fixes P1.5–P1.8 before treating the manifest and indexed routing
  as secure: recompute/stop trusting index coverage, split host-derived required capabilities from
  plugin-requested capabilities, verify native-terminal flags, and clean up indexed subscriptions on
  uninstall/reconnect.
- **1c.** Re-run CI for P1.4 and monitor the Windows smoke first. If it recurs, instrument IPC startup /
  named-pipe dispatch and capture the real remote exception; do not spend the unblock on §3.4 unless a new
  failure proves effect/capability drift.

**Step 2 — de-risk identity (§3.3).** Replace whole-compilation context inference with semantic paths:
same-compilation receiver server symbol (using `Context` when present, otherwise the current convention
context until §3.1 lands), same-compilation alias tracing from `server.Hooks`/`Subscriptions`, and prebuilt
SDK return type/public registry-marker metadata. Gate `CandidateKind` on generated metadata or server symbol
ownership, not member names or suffixes. The convention-named context stays until Step 4; only *identity
resolution* changes. Because prebuilt SDK / stored-registry support depends on the marker, Step 2 includes
`GeneratedPluginServerRegistryAttribute` / `GeneratedPluginServerRegistryKind`, generated registry emission,
and the matching `DotBoxD.Abstractions` API-baseline update.

**Step 3 — settle pre-baseline surface hazards.** Do P2.7–P2.11, P2.13, and P2.14 before any
API-baseline/package-smoke signoff for this work: delete or intentionally baseline generator plumbing,
restore/prove hook/subscription overload parity, remove/scope `DBXK110` with replacement diagnostics, decide
DBXK111/DBXK113 severity, add graft collision diagnostics, enforce plugin-owned receiver rejection plus
shared receiver-id metadata, and add wiring prevalidation/rollback. Step 2 already covers P2.4/P2.5; Step 4
covers P2.6 and P2.12 with §3.1/§3.4. Update API baselines and package schema/smoke expectations for any
metadata/signature changes these hazards require.

**Step 4 — the larger moves, as their own PRs.** §3.1 (author-declared context surface — only after the
attribute/factory/namespace/accessibility contract above is settled) and §3.4 (single-sourced
host-capability rule). Not bolted onto #88. The §3.1 PR also owns analyzer-visible SDK helper descriptors for
prebuilt context `[KernelMethod]` bodies; without that descriptor, metadata-only context helpers are rejected.
These PRs carry their own `DotBoxD.Abstractions` public API baseline updates for
`GeneratePluginServerAttribute.Context`, `ContextFactory`, `LocalAttribute`, `HostBindingEffect` /
`HostCapabilityAttribute` effect metadata, `GeneratedKernelMethodDescriptorAttribute`, and `[KernelMethod]`
XML docs, plus package metadata expectations for the generated descriptor attributes in packed SDKs.

> **Half-state note.** Merging #88 + Step 2 without Step 3 leaves the context still convention-named (its
> extension surface still undiscoverable) but with correct ownership-based identity. There is **no
> compatibility debt** — nothing ships to external users — so the only cost is the discoverability gap
> staying open until Step 4 lands. Acceptable as an interim state; just don't call the context "done" until
> the author-declared form (§3.1) replaces the convention partial.

**Step 5 — polish + docs smoke** (P3.15–19), ideally alongside the §3.4 duplication collapse so the
hook/subscription axis cannot drift again. No final API/package signoff after Step 3 happens until these
smoke checks are green; Step 4 PRs still update baselines for their own public API after their validation
passes.

**Validation checklist before handoff.** Mirror CI, not just the happy path:
`dotnet format whitespace DotBoxD.slnx --verify-no-changes --no-restore`;
`GITHUB_ACTIONS=true dotnet build DotBoxD.slnx -c Release`; targeted tests for changed runtime/analyzer
projects; `run-required-tests.ps1` with updated required-test names/minimums for new security regressions;
`check-rebrand-complete.ps1`; `check-csharp-file-lines.ps1`; `check-csharp-folder-layout.ps1`;
`check-spec-manifest.ps1`; `check-api-compat-baseline.ps1` or regenerated baselines for every touched
assembly; `check-release-workflow-security.ps1`; `check-release-readiness.ps1 -RequireComplete`;
`check-docs-smoke.ps1`; coverage collection for `tests/DotBoxD.Kernels.Tests` and
`tests/DotBoxD.Services.Tests` followed by `check-coverage.ps1`; package packing/metadata commands that
produce `artifacts/packages` and the CI expected version before `check-package-metadata.ps1`; package
consumer smoke including the SDK/plugin split; and the Windows GameServer smoke when P1.4 is in scope.

---

## Part 6 — Tests before accepting the direction

Acceptance gates, not afterthoughts. Existing homes: `ResultHookSlotTests`, `TypedServerContextTests`,
`HookPipelineFluentTests`.

**Already added**

- **Deterministic `BlinkBehindAsync` (P1.4).**
  [BlinkServerExtensionRegressionTests.cs](../../../samples/GameServer/Examples.GameServer.Plugin.Tests/Regression/BlinkServerExtensionRegressionTests.cs)
  installs + invokes the `[ServerExtension]` in-process under Auto/Compiled/Interpreted, asserting effects
  `[Concurrency, Cpu, HostStateRead, HostStateWrite]`, no `DBXK041`, and result `== 3`. It is in a
  `Regression/` subfolder so the test project root stays under the code-enforcer file budget. It does
  **not** cover the generated graft client or IPC transport path — those are separate gates below.

**Still required**

- **Cross-context result priority (P1.2).** Register a priority-`0` result handler in an earlier-created
  context pipeline and a priority-`100` handler in a later one for the same event; assert the `100` handler
  wins. Also cover equal-priority ties across context pipelines (global install order wins) and run both
  `FireAsync` overloads because they have separate multi-pipeline dispatch paths. Add configured-options
  coverage: when no explicit dispatch options are supplied, the winning handler uses its owning pipeline's
  `ConfigureResultDispatch` timeout/default-result behavior; explicit `FireAsync(..., options)` overrides
  those stored options. Add a context-factory preservation case where two context pipelines expose different
  context values and the winning result handler observes its own pipeline's context.
- **Conflicting context factories (P1.1).** Two `On<E, Ctx>(factoryA)` / `On<E, Ctx>(factoryB)` calls — for
  **both** hooks and subscriptions — throw on a conflicting factory; same factory/method-group reuse stays
  idempotent. Add both call orders for `On<E, HookContext>(...)` and `On<E>()` and assert no
  `InvalidCastException`.
- **Reusable-helper composition (P1.3).** Invoke one `RunLocal` helper twice and one `RegisterLocal` helper
  twice (two event sources each); assert both native handlers remain registered and fire. Verify server-side
  install ids and callback subscription ids differ, not just client-side dictionary entries, while the stable
  plugin/package principal used for policy, audit, server-extension dispatch, and replacement authority stays
  unchanged. Include setup recorder/replay cases for hook, subscription, and result-local terminals so
  setup-authored local handlers are registered under the real callback id, not the source id, before server
  routing activation. Add an immediate-dispatch regression where an event/result callback fires at the point
  old code would have returned from install but before client registration completed, plus a
  handler-registration-failure rollback case. Add teardown and replacement coverage: install two
  same-principal local-helper instances, then dispose, uninstall, or hot-replace one instance and assert only
  that instance's hook/subscription/result-local registrations are removed. Include the indexed path too:
  publish through the index after removing one same-principal indexed subscription and assert the sibling
  instance remains routed.
- **Index-coverage tamper (P1.5).** Tamper a package manifest to claim `IndexCoversPredicate = true` for a
  partial predicate; assert validation rejects it or dispatch still runs `ShouldHandleAsync`. Cover both
  exported JSON tamper/import/install and direct in-memory `PluginPackage` tamper so the boundary under test
  is real.
- **Capability policy split (P1.6).** A package with host-derived capabilities plus plugin-declared
  capability requests must pass `DBXK044` only for the host-derived set; requested capabilities are denied
  unless an independent server allowlist grants them. Add a codegen assertion that generated hook/chain
  modules no longer mirror host-derived requirements into `CapabilityRequests`, plus package JSON roundtrip,
  schema-drift, and API-baseline coverage for any manifest/model shape change.
- **Native-terminal flag tamper (P1.7).** Tamper `LocalTerminal`, `ProjectedType`, `ResultType`, and
  `ResultLocalTerminal` through JSON import and direct package mutation; assert install rejects divergent
  terminal-kind metadata or uses install-path-owned expected terminal metadata before routing to native
  callbacks. If a new trusted metadata source is added, mutate that too and prove it is authenticated or
  cross-checked.
- **Indexed subscription cleanup (P1.8).** Install an indexed subscription, dispose/reinstall the session or
  reconnect, then publish through the world index and prove the old indexed kernel is gone. Also cover hot
  replace/kernel replacement; both removal paths must release indexed registrations.
- **Required security test gate.** Add the P1.5–P1.8 tests to CI's `run-required-tests.ps1` names/minimums (or
  raise the relevant class minimums) so the security gate proves these regressions still run.
- **Foreign `.Hooks.On` negative analyzer fixture (P2.4).** A non-DotBoxD fluent API exposing
  `Hooks.On<T>()` / `Subscriptions.On<T>()` (and a type named `…HookRegistry`) is **not** intercepted.
  Include direct receiver and aliased registry variables. Add positive same-compilation alias coverage for the
  planned alias tracing, plus prebuilt-SDK alias coverage for marker metadata. Add marker-contract negatives:
  inherited markers, duplicated markers, malformed constructor arguments, and user-authored lookalikes without
  the required server-facade/property/context ownership proof are ignored or diagnosed rather than trusted.
- **Multi-server context ownership (P2.5).** Two `[GeneratePluginServer]` types in one compilation each
  resolve their own generated context for parameterless `On<TEvent>()` in both interim convention mode and
  future `[GeneratePluginServer(Context = ...)]` mode; a prebuilt SDK referenced cross-assembly resolves from
  generated return types/marker metadata.
- **Host-binding descriptor parity (P2.6 / §3.4).** Analyzer-derived route + effects equal runtime-derived
  route + effects for the same `[DotBoxDService]` method (guards the `DBXK041` seam at compile time). Expand
  this to required capability, async flag, parameter/return shape ids, and the binding-kind-specific
  metadata precedence, including handle methods where interface metadata is authoritative.
- **Typed hook/subscription overload parity (P2.8).** `RemoteHookPipeline.Typed` and
  `RemoteSubscriptionPipeline.Typed` (and the `…Stage.Typed` pair) expose the same `UseGeneratedLocalChain`
  set, including the two element-only forms.
- **`DBXK110` non-emission (P2.9).** A combined analyzer+generator test proves a `Run(lambda)` site the
  generator lowers does **not** emit `DBXK110`; an unlowerable recognized `Run(lambda)` chain emits the
  specific replacement diagnostic. Update/invert the existing detection test and analyzer API baseline if
  the public descriptor is removed.
- **`DBXK111`/`DBXK113` generated-facade coverage (§3.2/P2.4).** Same-compilation generated
  `server.Hooks...RunLocal` failures emit the planned warning, not a silent runtime-only failure.
- **`DBXK113` severity.** Result `Register`/`RegisterLocal` non-lowered cases emit the planned warning
  severity, with analyzer release/baseline updates if descriptor surface changes.
- **Subscription cancellation (P2.10).** Pre-canceled publish does not run local handlers, and caller-token
  cancellation is not reported as a plugin handler/filter fault. Cover both broad subscription publish and
  indexed subscription dispatch.
- **Context contract diagnostics (§3.1).** Missing `Context`, non-partial/generic/nested context types, wrong
  namespace emission, context accessibility below the generated surface, invalid `ContextFactory`, and
  duplicate generated context augmentation produce clear diagnostics.
- **Context `[HostBinding]` rejection (§3.1/§3.2).** A plugin-declared `[HostBinding]` member on the context is
  rejected; host access goes through re-exposed `[DotBoxDService]` selectors only.
- **`[Local]` and service-selector misuse (§3.2).** `[Local]` outside the declared context, `[Local]` members
  used in lowered stages or lowered `[ServerExtensionMethod]`/RPC bodies, and `RunLocal`/`RegisterLocal` or
  `[Local]` members attempting `ctx.World.*` produce the planned diagnostic. Add a positive server-authored
  SDK `[Local]` context helper case and a negative plugin-assembly attempt to add a native context helper, so
  the SDK split is explicit.
- **Static `[KernelMethod]` helper scope (§3.2).** Plugin static helpers with scalar parameters/returns inline
  successfully; static helpers that take a context/service parameter fail with the planned diagnostic unless
  a future PR implements explicit context rebinding and adds the corresponding positive/negative tests.
- **Prebuilt SDK context `[KernelMethod]` descriptors (§3.2/§3.1).** A plugin project referencing a prebuilt
  SDK calls a server-authored context `[KernelMethod]` helper and the analyzer consumes the SDK helper
  descriptor/IR. Include a helper that calls a re-exposed `[DotBoxDService]` selector and assert the generated
  package carries the descriptor's transitive capabilities/effects and installs without `DBXK041`/`DBXK044`.
  Cover `this.World...` and implicit `World...` receiver rebinding in same-compilation descriptor generation
  and prebuilt SDK consumption. A metadata-only helper with no descriptor fails with the planned diagnostic.
  Emit and consume two helper descriptors from one SDK assembly so `AllowMultiple = true` is proved. Tamper /
  negative cases: plugin-forged descriptors, stale descriptor hashes, mismatched signatures or context types,
  descriptor IR with local/native escapes, descriptors that inject host calls outside the server-owned
  selector contract, and descriptors whose verified IR calls a host selector while the serialized
  capability/effect metadata omits or weakens that requirement are rejected.
- **Generated graft collision (P2.11).** Two `[ServerExtension]` kernels grafting the same method
  name/signature onto the same receiver in the same namespace produce a diagnostic.
- **Server-extension receiver contract (P2.13).** Plugin-owned receiver grafts are rejected for the safe
  extension surface; client receiver-id argument shape must come from the same server-owned graft metadata as
  the server package.
- **GameServer wiring rollback (P2.14).** Force post-install hook/subscription/result/index wiring failure for
  a supported-event package through a real throwing wiring path (for example, `RegisterLocal` without callback
  transport) and assert routing validation happens before install or the install is rolled back with no stale
  kernel left in the session/server. Include a hot-replace case: install a valid kernel, attempt same-plugin
  replacement that fails wiring, and assert the original kernel remains installed and routed.
- **`[ServerExtension]` graft from a real plugin assembly.** A plugin project that references a *prebuilt*
  SDK (no `[GeneratePluginServer]` of its own) authors a `[ServerExtension(typeof(IMonster))]` +
  `[ServerExtensionMethod]`, and it lowers, emits the grafted extension in the plugin's own namespace,
  installs under policy, and invokes through the **generated extension method** in-process **and** over IPC.
  Include generated-context parameter support if §3.1 has landed; keep a raw `HookContext` escape-hatch case.
- **Grafted extension composes inside a chain.** A `[ServerExtension]` method called inside a lowered
  `.Run`/`.Where` lowers and runs server-side (no extra roundtrip); the same method called standalone goes
  over IPC. Both paths return the same result (proves Q4).
- **Server SDK context discoverability.** Assert the server author reaches the context extension point from
  the author-declared context type attached via `[GeneratePluginServer(Context = typeof(...))]`, with no
  reference to a name-derived `{Root}Context` type.
- **Plugin-dev graft discoverability.** Assert the plugin dev reaches grafted operations as generated
  extension methods in their namespace, not by knowing convention-generated server types.
- **Plugin-fluent docs smoke.** Add these design docs to a stale-term / compiled-snippet check so
  stale `server.Events.On` / fire-and-forget prose, stale `server.Kernels.Register`, `InvokeKernel`,
  `InvokeLocal`, stale live-settings APIs, moved source links, and broken line/citation links cannot regress
  unnoticed. Use a curated plugin-fluent doc list; do **not** repo-wide ban `server.Events` because
  `server.Events.Resolve<TEvent>()` is still current, and do **not** repo-wide ban `Invoke*` because
  `InvokeAsync` is legitimate.
- **Package-consumer SDK split smoke.** Extend the package smoke with two temp projects: an SDK/facade project
  built first, then a plugin project with only package references that authors the grafted extension and
  invokes it over IPC. In the same smoke, store/alias `server.Hooks` and `server.Subscriptions` from the
  packed SDK and author a lowered hook/subscription chain so the generated registry marker attribute/enum are
  proven visible and honored through `PackageReference`, not only project-reference analyzer tests. Also have
  the packed SDK define two server-authored context `[KernelMethod]` helpers, including one that calls a
  host-service selector, and have the plugin consume both in lowered hook/subscription chains. This proves
  repeatable helper descriptor attributes are packed, package-metadata-approved, visible, and merged into
  generated caps/effects.

---

## How this doc was reviewed

Cross-checked through independent review lenses — *Simple, Obvious, Discoverable, Consistent, Minimal,
Composable, Explicit, Stable, Testable, Boring* — plus correctness audits against head `41ec9172`. Where a
reviewer's proposed fix was itself wrong, the corrected version is what appears above — specifically:
the P1.2 "merge-sort by `(priority, order)`" fix (rejected: `_order` is per-`ResultHookSlot`, not global —
[ResultHookSlot.cs:25,235](../../../src/Hosting/DotBoxD.Plugins/Runtime/Hooks/ResultHookSlot.cs)), and the
§3.4 "remove the `DBXK041` drift class entirely" framing (rejected: `DBXK041`/`DBXK044` are the
trust-boundary cross-check — [PluginPreparedPackageValidator.cs:23-27](../../../src/Hosting/DotBoxD.Plugins/Runtime/Validation/PluginPreparedPackageValidator.cs)).
