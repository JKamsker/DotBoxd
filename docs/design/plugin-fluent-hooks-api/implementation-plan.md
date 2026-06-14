# Implementation plan (critique-informed, authoritative)

This is the **authoritative** plan after two adversarial self-review rounds. Where it disagrees with an
earlier doc ([plan.md](plan.md), [ownership-auth-and-policy.md](ownership-auth-and-policy.md),
[kernel-binding-model.md](kernel-binding-model.md), [capability-gating.md](capability-gating.md), the
walkthroughs), **this doc wins** — those remain as design rationale but contain sketch code and a few
claims the reviews corrected (noted inline there).

## Implementation status

| Phase | Status |
|---|---|
| **A** — rename, Program classes, slnx, cleanup | ✅ done, example runs e2e |
| **B2** — convention adapters (delete hand-written), shape wiring via `On<TEvent>()` | ✅ done |
| **B1** — ownership: `PluginSession`, owner-checked registry (DBXK060), revoke-on-disconnect (`RpcPeer.Disconnected`), 4 concurrency fixes, owner-checked settings (DBXK061) | ✅ done, tested |
| **B (fluent surface)** — `HookStage<TEvent,TCurrent>`, `Select`, `InvokeLocal`, `InvokeKernel`(throws DBXK062 until lowered) | ✅ done, tested |
| **B4** — service contracts (`IFooService : IEventKernel<TEvent>`), `Kernels.Register<TService,TKernel>()`, `Get<TKernel>().SetValuesAsync`, `KernelPackageRegistry` | ✅ done, example uses it e2e |
| **B3 (core)** — hierarchical/wildcard capability matching (`game.world.monster.*`) | ✅ done, tested |
| **C-0** — analyzer detection of un-lowered `InvokeKernel(lambda)` chains (DBXK110) | ✅ done, tested |
| **C-1** — kernel-type → package resolution | ✅ done via reflection-based `KernelPackageRegistry` (the `[ModuleInitializer]` emit is the optional AOT path, deferred to avoid generator/golden-snapshot churn) |
| **C-2 / C-3** — **lower** `Where`/`Select`/`InvokeKernel` lambda bodies to verified DotBoxd.Kernels | ✅ **done, tested** — `HookChainModelFactory` lowers `On<TEvent>().Where*(lambda).Select*(lambda).InvokeKernel((e,ctx)=>ctx.Messages.Send(...))` into a `HookChain_<id>PluginPackage` through the existing emitter/verifier. All `Where`s AND-compose into `ShouldHandle`; a `Select` projection substitutes into downstream `Where`/`Send` at compile time (lowering-context projected-element binding); the terminal `Send` becomes `Handle`. Subset: expression-body lambdas, single `Send`; other shapes fail safe. The runtime hook-up is complete too — `DotBoxdHookChainInterceptorEmitter` emits a C# *interceptor* (preview) so the `InvokeKernel` call site installs+wires its generated chain package (`UseGeneratedChain`) instead of throwing `DBXK062`. |
| **B3 (capability gating)** — wildcard-gated install enforcement | ✅ **done, tested** — `CapabilityPattern` + `PolicyResolver`/`PolicyGrantValidator` authorize a concrete required capability under a wildcard grant and fail closed otherwise. **Example demo done, e2e:** `[HostBinding(id, capability, effects)]` host-service methods (`DotBoxdHostBindingExpressionLowerer`) lower a `ctx.Host<T>().M(args)` call to a `CallExpression(id, args)`; the analyzer derives `RequiredCapabilities` (host-binding caps, `[Capability]`-gated event-property reads, the message-write Send cap) and the host-binding effects into the manifest. `IGameWorldAccess.GetHealth` lowers to `host.world.getHealth`; the server builds a per-kernel least-privilege policy from each manifest's `RequiredCapabilities`, so the guardian gets `game.world.monster.read.*` (matching `…read.health`) and a write/threat kernel is denied at install. Verified by `PluginAnalyzerHostBindingTests` (7) and the GameServer example (exit 0). |
| **Auth/signing/policy-resolver** | deferred appendix (no consumer; see §below) |

Every shipped item above is committed with a green `dotnet build DotBoxd.Kernels.slnx -c Release` and a green
`tests/DotBoxd.Kernels.Tests` run.

## What the reviews changed (the short version)

- **Cut the security platform.** Auth / mTLS / cert signing / `SignedPluginGrant` / `IPluginPolicyResolver`
  / requested-vs-granted have **zero consumer**: the example is single-tenant, single named-pipe
  connection, and the server *builds the only package it installs*. The manifest cannot widen anything
  today (it has no limits field). → **Deferred to an appendix** behind a real trust-boundary story.
  Ownership uses **the session object itself as identity** — no `PluginIdentity`/authenticator needed.
- **`peer.OnDisconnected` is fictional.** DotBoxd exposes no such hook in our transport. Revoke-on-disconnect
  must bind to the **real** `RpcPeerSession`/host lifecycle (verify at implementation) or a
  **session-owned heartbeat + absolute TTL** fallback — specified, not deferred.
- **Generic wiring "by event-name string" cannot compile.** `HookRegistry.On` is generic-only and
  `TEvent` is erased through a string. → wire through a **new internal non-generic, shape-based path**.
- **Four ownership concurrency defects** must be fixed (linked revoke token, install/dispose race,
  same-owner hot-reload, `PluginServer.Dispose` revoke).
- **Honest surfaces:** `InvokeKernel(lambda)` is `[Obsolete(error:true)]` today — it is **Phase C**, not
  current. The awaitable struct builder silently drops installs → ship a **`Task`-returning**
  `Register(where:)`. `[OpaqueId]` collides with an IR concept → **`[SandboxOpaqueId]`**.
- **Capability gating reuses the existing binding/capability model** (every host call already gates on a
  `RequiredCapability`); the new work is wildcard matching, gated event-properties, analyzer-derived
  manifest caps, and deny-or-disconnect.

---

## Locked decisions (final)

| Area | Decision |
|---|---|
| Pre-kernel gate | fluent `.Where(...)`, lowered (Phase C). No `filter:` parameter. |
| Kernel binding | `server.Kernels.Register<TService, TKernel>()` + `Register<TKernel>()` overload; `Task`-returning `Register(where:)` for the gate (no awaitable-struct builder). |
| Service contract | `interface IFooService : IEventKernel<TEvent>`; kernels implement the contract. Event detection is free (transitive `AllInterfaces`); emitting the **contract name** into the manifest is a new analyzer output (`ServiceContract` field — additive; existing `Contract` stays `IEventKernel<…>`). |
| Wiring | internal **shape-based** non-generic path (no `ResolveByEventName`, no `On(adapter)` non-generic). Deletes `WireHook`'s switch. |
| Adapters | convention adapter (already exists) is the default; **delete** the example's hand-written adapters and **rewrite** `WireHook`/`Program` that reference `.Instance`. |
| Ownership | `PluginSession` (the object **is** the identity/`OwnerId`); owner-checked `KernelRegistry` (fail-closed `DBXK060`); owner-checked settings; revoke-on-disconnect; the four concurrency fixes. |
| Typed access | `Get<TKernel>()` (plugin-side), `Get(string)` (both), `GetAll<TService>()` primary server-side (per-user → many), `Get<TService>()` singleton-only (throws on ambiguity). `SetValuesAsync(Action<T>)` over a generated **draft factory** (no `new()` constraint). |
| Capabilities | hierarchical ids + wildcard **grants**; `[Capability]` on ctx bindings & event properties; analyzer-derived `RequiredCapabilities`; **deny at install** + runtime backstop → configurable disconnect+unload. Default-allow (unannotated = open). |
| Auth/signing/policy-resolver | **deferred appendix** — not built now. |
| `Program` (both) | full `internal static class Program` with `Main`. |

---

## Phasing (realistic, each phase builds & is independently shippable)

### Phase A — example cleanup, renames, Program classes, slnx  *(low risk; this session)*
No framework/API changes; keeps the current functional install path working.
- Rename `examples/GameServer/DotBoxd.Kernels.Game.PluginHost` → `DotBoxd.Kernels.Game.Plugin` (folder, csproj, namespaces,
  generated package namespaces).
- Server: `PluginHostLauncher` → `PluginLauncher`; constants/env var (`SAFEIR_GAME_PLUGINHOST_DLL` →
  `SAFEIR_GAME_PLUGIN_DLL`); update call site.
- Delete `Local/LocalPreview.cs`, `Local/PluginHostPolicy.cs`, `Local/RecordingMessageSink.cs`.
- Both `Program` → full `internal static class Program` with `Main`; preserve exit-code contract.
- `DotBoxd.Kernels.slnx` nested solution folders mirroring disk.
- Update `scripts/check-docs-smoke.ps1`, `docs/Specs/Addendum/Examples.md`, `README.md`.
- **Verify:** `dotnet build DotBoxd.Kernels.slnx -c Release`; run the server example end-to-end (baseline +
  with-plugin phases, exit 0); `./scripts/check-docs-smoke.ps1 -Configuration Release`.

### Phase B — fluent surface, ownership, convention wiring, capability gating  *(framework work in `src/`)*
1. **Convention wiring** — delete the example adapters; rewrite `WireHook` to the internal shape-based
   path (`PluginEventAdapterRegistry.TryResolveShape` + a new `UseKernelByShape(InstalledKernel, shape)`
   on `HookRegistry`). Keep `On<TEvent>()` for the chain.
2. **Ownership** (`src/DotBoxd.Plugins`):
   - `OwnerId` on `InstalledKernel` (ctor); `KernelRegistry.Add` fail-closed on cross-owner id (`DBXK060`),
     same-owner reinstall revokes the prior incumbent (capture in-lock, revoke out-of-lock).
   - `PluginSession : IDisposable` (+ `IAsyncDisposable`), the object is the `OwnerId`; `CreateSession`,
     `InstallOwnedAsync`, `UninstallOwned`; install/dispose atomic via a session gate.
   - **Concurrency fixes:** link `_revocation.Token` into every `_executionGate.WaitAsync`;
     `PluginServer.Dispose` revokes all kernels before disposing the host; `KernelRegistry.Remove`/session
     dispose rebuild the COW handler array (drop stale handlers).
   - Owner-checked `UpdateSettingsAsync` (route the control service through the session).
   - **Disconnect:** bind to the real `RpcPeerSession`/host lifecycle (verify the API; the per-peer
     control service is created in `ForEachPeer`) **or** session heartbeat + absolute TTL fallback.
3. **Service-kernel API** — `Kernels.Register<TService,TKernel>()` + `Register<TKernel>()` (infer
   `TService` by walking interfaces like `PluginSymbolReader.EventTypes`); `Task`-returning
   `Register(where:)`; `Get<TKernel>()`, `GetAll<TService>()`, `Get<TService>()` (singleton), draft
   factory for `SetValuesAsync` (no `new()`).
4. **Capability gating** — `CapabilityPattern` wildcard matcher; extend `SandboxPolicy.GrantsCapability`
   (wildcard bucket); example game-world ctx bindings (`IGameWorldAccess` → `game.world.*` bindings with
   `RequiredCapability`); `ServerPolicy` grants `game.world.monster.*` (read) to guardian; install-time
   capability check (deny) + runtime backstop → `CapabilityViolationResponse`.
- **Verify:** `tests/DotBoxd.Kernels.Tests` green + new unit tests (owner fail-closed, revoke-unblocks-waiter,
  wildcard match, deny-on-missing-capability, settings owner-check); update `DotBoxd.Plugins` API baseline.

### Phase C — analyzer lowering  *(large, highest risk; sub-phased C-0…C-3 per [plan.md](plan.md))*
- Lower `Where`/`Select`/`InvokeKernel` chain lambdas to verified IR (un-obsolete a **lowered**
  `InvokeKernel` terminal; keep `InvokeLocal` native).
- Emit `ServiceContract` name + analyzer-derived `RequiredCapabilities` (incl. gated event-property
  reads) into the manifest.
- **Verify:** incrementality + golden-snapshot tests; an end-to-end chain that lowers, ships, runs
  sandboxed, and is denied when a required capability is missing.

### Deferred appendix — auth, signing, per-plugin policy resolver
Build only behind a real trust boundary (a TCP/mTLS transport, third-party package distribution, or
multi-tenant limits). If built: async `IPluginAuthenticator`, normative `PackageHash`, replay/expiry/
revocation on the grant, `RequireSignedGrant=true` default, per-field `ClampTo` (not blanket `Math.Min`),
`PipeOptions.CurrentUserOnly` + CSPRNG bootstrap token over stdin. See
[ownership-auth-and-policy.md](ownership-auth-and-policy.md) §4 for the full (deferred) design.

---

## Corrections folded in from the reviews (checklist)

- [ ] `peer.OnDisconnected` replaced by the real lifecycle API or heartbeat/TTL (R2-2.1).
- [ ] `KernelRegistry.Add` owner fail-closed **and** same-owner hot-reload revoke (R2-2.2/2.3).
- [ ] install/dispose atomic; no post-await leak (R2-2.4).
- [ ] `_revocation.Token` linked into `_executionGate.WaitAsync` (R2-2.5).
- [ ] `PluginServer.Dispose` revokes kernels before host teardown (R2-2.6).
- [ ] stale COW handler removed on revoke/remove (R2-2.8).
- [ ] owner-checked `UpdateSettingsAsync` (R2-4.9).
- [ ] adapter deletion ships with the `WireHook`/`Program` rewrite, else build breaks (R2-3.1).
- [ ] `[OpaqueId]` → `[SandboxOpaqueId]` (R2-3.2).
- [ ] generic wiring → internal **shape-based** path; no `ResolveByEventName`/non-generic `On` (R3-C1).
- [ ] `InvokeKernel(lambda)` documented as Phase-C (un-obsoletes the current `error:true` forwarder) (R3-H1).
- [ ] `Register(where:)` is **`Task`-returning**; no silent-drop awaitable struct (R3-H2).
- [ ] `ServiceContract` is a **new additive** manifest field needing analyzer emission (R3-H3).
- [ ] drop `new()`; generated draft factory for `SetValuesAsync` (R3-H4).
- [ ] shim/example code marked **design sketch** where it names Phase-B/C types (R3-H5).
- [ ] `GetAll<TService>()` primary; `Get<TService>()` singleton-only (R3-M1).
- [ ] `Register<TKernel>()` single-arg overload (R3-M2).
- [ ] capability ids: lowercase dotted grammar; wildcard only on **grants**, requirements concrete (R4).
