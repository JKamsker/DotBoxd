# DotBoxd.Kernels: Plugin example cleanup + fluent Hooks/Events API + kernel auto-install

## Context

The `DotBoxd.Kernels.Game.PluginHost` example and the server-side hook API don't yet match the
plugin-authoring experience the user wants. Today the plugin process manually exports each
kernel to JSON and ships it with explicit "opaque verified IR" commentary
(`PluginPackageJsonSerializer.Export(GuardianPluginPackage.Create())` →
`service.InstallPluginAsync(json)`), carries its own duplicate sandbox policy
(`PluginHostPolicy`, identical to the server's `ServerPolicy`), runs an in-process
`LocalPreview`, and uses top-level statements for `Program`. The slnx lists examples flat
even though they are nested in category folders on disk.

The user wants:
- The plugin to *declare* hooks with the same fluent API the server uses
  (`server.Hooks.On<MonsterAggroEvent>().UseKernel<GuardianKernel>()`), with the framework
  shipping/installing the kernel IR automatically — no manual `Export`/`InstallAsync`, no
  "opaque IR" narration in the example (the shipping APIs stay, they're just hidden).
- `PluginHost` renamed to `Plugin` (it *is* the plugin); policy ownership on the server.
- A richer pipeline: `On<TEvent>().Where(..).Select(..).Where(..).InvokeKernel|InvokeLocal(..)`
  where every `Where`/`Select`/`Invoke` lambda takes `(element, HookContext)`. `Where`/`Select`/
  `InvokeKernel` lambdas run **sandboxed (lowered to verified IR)**; `InvokeLocal` is native host
  code. `UseKernel<T>()` gains an optional filter (gates in addition to the kernel's `ShouldHandle`).
- `server.Events` as a fire-and-forget mirror of `server.Hooks` (Hooks = plugin decides what to
  do; Events = plugin is just notified).
- The architecture stays **two processes**; the slnx solution folders mirror the on-disk nesting.

This is large. It is phased so each phase builds, tests green, and is independently shippable.
Phases A–B deliver the visible request and the fluent API; Phase C is the heavy analyzer work.

---

## Phase A — Example cleanup, rename, slnx (low risk, no framework changes)

Delivers the rename, `Program` class, policy-on-server, and slnx nesting. Uses a small
example-local client shim so the plugin reads `server.Hooks.On<>().UseKernel<>()`.

### A1. Rename `DotBoxd.Kernels.Game.PluginHost` → `DotBoxd.Kernels.Game.Plugin`
- Folder + csproj: `examples/GameServer/DotBoxd.Kernels.Game.PluginHost/` → `examples/GameServer/DotBoxd.Kernels.Game.Plugin/`,
  `DotBoxd.Kernels.Game.PluginHost.csproj` → `DotBoxd.Kernels.Game.Plugin.csproj` (assembly/DLL follow automatically).
- Namespace `DotBoxd.Kernels.Game.PluginHost` → `DotBoxd.Kernels.Game.Plugin` in `Kernels/GuardianKernel.cs`,
  `Kernels/RetaliationKernel.cs`, `Program.cs` (and any kept files). Generated
  `GuardianPluginPackage`/`RetaliationPluginPackage` move to the new namespace automatically.
- Server side: `examples/GameServer/DotBoxd.Kernels.Game.Server/Ipc/PluginHostLauncher.cs` → rename file +
  type to `PluginLauncher`; constants `HostProjectDir`→`"DotBoxd.Kernels.Game.Plugin"`,
  `HostDllName`→`"DotBoxd.Kernels.Game.Plugin.dll"`, env var `SAFEIR_GAME_PLUGINHOST_DLL`→`SAFEIR_GAME_PLUGIN_DLL`.
  Update the call site in `DotBoxd.Kernels.Game.Server/Program.cs` (`PluginLauncher.Launch`).
- Scripts/docs: `scripts/check-docs-smoke.ps1` (env var ×3, dll path, csproj path),
  `docs/Specs/Addendum/Examples.md` (line ~515 path + reword preview/opaque-IR prose), `README.md`
  (prose ~290–296). **Do not** touch `external/dotboxd/**` or `artifacts/**` (coincidental matches).

### A2. Remove the local-preview / duplicate-policy artifacts
Delete `Local/LocalPreview.cs`, `Local/PluginHostPolicy.cs`, `Local/RecordingMessageSink.cs` and the
empty `Local/` folder. Rationale: `PluginHostPolicy` duplicates `ServerPolicy` (policy is the
server's job); `LocalPreview` is the only consumer and the only in-process `PluginServer` in the
plugin; `RecordingMessageSink` is dead once preview is gone. Keep all csproj references
(`DotBoxd.Plugins`, `DotBoxd.Kernels.Serialization.Json`, `DotBoxd.Pushdown.Services`, the
`DotBoxd.Plugins.Analyzer` analyzer ref, `DotBoxd.Kernels.Game.Server.Abstractions`) — the analyzer still
generates the packages the shim ships.

### A3. `Program` becomes a full class
Rewrite `Program.cs` as `internal static class Program` with `static async Task<int> Main(string[])`,
preserving the exit-code contract. New flow: validate args → connect pipe → wrap connection in the
shim → `server.Hooks.On<MonsterAggroEvent>().UseKernel<GuardianKernel>()` /
`On<AttackEvent>().UseKernel<RetaliationKernel>()` → tune live settings fluently → exit. No
`LocalPreview`, no explicit `Export`/`InstallPluginAsync`, no "opaque IR" comments.

### A4. Example-local client shim `RemotePluginServer`
New `examples/GameServer/DotBoxd.Kernels.Game.Plugin/Client/RemotePluginServer.cs` exposing a server-shaped
surface (`Hooks.On<TEvent>().UseKernel<TKernel>()`, `Kernels.Get(id).Set(..).ApplyAsync(atomic:)`)
that maps onto the existing, unchanged `IGamePluginControlService` IPC contract
(`InstallPluginAsync`, `UpdateSettingsAsync`). `UseKernel<TKernel>()` is `async` (a real IPC
round-trip). Package resolution: in Phase A use a small `KernelPackageCatalog` switch
(`typeof(GuardianKernel)` → `PluginPackageJsonSerializer.Export(GuardianPluginPackage.Create())`).
In Phase B this delegates to the generated registry (B4) so the catalog can be deleted.

### A5. slnx nesting
Edit `DotBoxd.Kernels.slnx`: replace the flat `/examples/` block with nested solution folders
(`/examples/Capabilities/`, `/examples/GameServer/`, `/examples/Hosting/`, `/examples/HttpTransport/`,
`/examples/LocalPlugin/`, `/examples/PluginAuthoring/`, `/examples/PluginIpc/`) mirroring disk; keep an
empty parent `/examples/`. Only the GameServer plugin project *path* changes (the rename); all other
paths stay, only their containing `<Folder>` changes. `/src/`, `/tests/`, `/benchmarks/`, `/tools/`
untouched.

**Phase A verify:** `dotnet build DotBoxd.Kernels.slnx -c Release`;
`dotnet run --project examples/GameServer/DotBoxd.Kernels.Game.Server -c Release` (server self-launches the
renamed plugin; exit 0, baseline + with-plugin phases print); `./scripts/check-docs-smoke.ps1 -Configuration Release`.

---

## Phase B — Server-side fluent API: Select, InvokeLocal/InvokeKernel, UseKernel filter, auto-install, server.Events

Framework work in `DotBoxd.Plugins`. No lambda lowering yet — `InvokeKernel(lambda)` is defined but
throws at runtime until Phase C rewrites it (the analyzer turns it into `UseKernel<T>()`).

Primary file: `src/DotBoxd.Plugins/Runtime/HookRegistry.cs`. Also `PluginServer.cs`, plus new
`Runtime/KernelPackageRegistry.cs` and `Runtime/EventRegistry.cs`.

### B1. Staged builder for `Select` (the core re-typing)
Keep the registered pipeline keyed by the adapter's `TEvent` (publish input / registry key /
adapter type must stay fixed). Introduce `HookStage<TEvent, TCurrent>` where `TCurrent` is the
element currently flowing. Each stage carries one composed projection
`Func<TEvent, HookContext, ValueTask<(bool ok, TCurrent value)>>`; `Where`/`Select` return a *new*
stage; only terminals mutate the root's handler list. `On<TEvent>()` still returns
`HookPipeline<TEvent>` (no signature break); `.Select(..)` transitions into the stage.
**Risk:** do not re-key pipelines by `TCurrent`. `Where` placed before any `Select` keeps current
root-level short-circuit semantics; per-terminal short-circuit only after a `Select`.

### B2. `InvokeLocal` / `InvokeKernel` terminals
`InvokeLocal` = current `InvokeHostHandler` (native host delegate); make `InvokeHostHandler` an
`[Obsolete]` (non-error) forwarder. `InvokeKernel((e,ctx)=>..)` exists as the API the analyzer
lowers; its runtime body **throws** a clear `SandboxValidationException` (e.g. `DBXK040`,
"must be lowered by DotBoxd.Plugins.Analyzer") so un-lowered plugin code never runs unsandboxed.
Remove the old obsolete-error `InvokeKernel` overloads.

### B3. `UseKernel<TKernel>(optional filter)`
Add optional `Func<TEvent,HookContext,bool>? filter` (and async overload) that gates before the
sandbox; the kernel's own `ShouldHandle` still runs inside. Expose `UseKernel` only where
`TCurrent == TEvent` (kernels consume the original event via the adapter).

### B4. Auto-install on `UseKernel<TKernel>()`
New static `KernelPackageRegistry` mapping `Type` → `Func<PluginPackage>`. The analyzer emits a
`[ModuleInitializer]` in each generated `{X}PluginPackage` that self-registers
(`typeof(GuardianKernel)` → `Create`) — see C/B-bridge. `UseKernel<TKernel>()` changes from
"throw if missing" to: `KernelRegistry.TryGetByKernelType<T>` → else
`KernelPackageRegistry.TryGetFactory` → `InstallAsync` → wire. Thread a
`Func<PluginPackage, ValueTask<InstalledKernel>>` (bound to `PluginServer.InstallAsync`) into
`HookRegistry`/`EventRegistry`. Add sync (`UseKernel<T>()`, blocks on install at setup time) and
`UseKernelAsync<T>()`. Add `KernelRegistry.TryGetByKernelType<T>(out InstalledKernel)`.
This emit is small and zero-lowering — land it here so Phase A's `KernelPackageCatalog` can be deleted.

### B5. `server.Events` fire-and-forget mirror + name-collision fix
Rename the current `PluginServer.Events` (the adapter registry) to `EventAdapters`; repoint
`RegisterEventAdapter` and internal uses; update `docs/api-baselines/DotBoxd.Plugins.txt`. Introduce a
new `Events` property of type `EventRegistry` with `On<TEvent>()` → `EventPipeline<TEvent>`. Share the
`HookStage` machinery via a small `IHandlerSink<TEvent>` interface implemented by both
`HookPipeline<TEvent>` and `EventPipeline<TEvent>` so `Where`/`Select`/`InvokeLocal`/`InvokeKernel`/
`UseKernel` are written once. Difference: `EventPipeline.PublishAsync` is fire-and-forget and
isolates handler exceptions; `HookPipeline.PublishAsync` awaits sequentially (decisions matter).
Both share `EventAdapters` + `KernelRegistry`.

**Phase B verify:** existing `tests/DotBoxd.Kernels.Tests` pass (obsolete forwarders keep examples compiling);
add unit tests for `Select` re-typing, `UseKernel` filter, auto-install resolution, and
`Events` fire-and-forget vs `Hooks` await. Update `DotBoxd.Plugins` API baseline.

---

## Phase C — Analyzer lambda lowering (large, multi-phase, highest risk)

Lower `Where`/`Select`/`InvokeKernel` inline lambdas in `server.Hooks`/`server.Events` chains to
verified DotBoxd.Kernels; leave `InvokeLocal` native. Primary project: `src/DotBoxd.Plugins.Analyzer`.
Key reuse: the existing lowerer (`DotBoxdExpressionModelFactory`, `DotBoxdConditionBodyModelFactory`,
`DotBoxdHandleModelFactory`) and emitter (`DotBoxdPackageSourceEmitter`) already lower
`ShouldHandle`/`Handle` method bodies — reuse them unchanged.

**Key design decision:** treat `Select` projection as **compile-time substitution** into downstream
lambda lowering, not a new runtime value-passing protocol. This keeps the existing two-entrypoint
(`ShouldHandle`/`Handle`) module contract, helpers, verifier, and validator unchanged — all new
complexity stays in the analyzer.

### C1. Detection (new second generator branch)
Add a `CreateSyntaxProvider` in `DotBoxdPluginPackageGenerator.cs` keyed on invocation syntax
(predicate: member-access named `Where`/`Select`/`InvokeKernel`, allocation-free). Semantic
transform: require the receiver type to be `DotBoxd.Plugins.HookPipeline<T>`/`EventPipeline<T>`
(rejects LINQ), transform **only the terminal** node, walk down the receiver chain to the
`On<TEvent>()` seed, collect ordered stages + lambdas. Terminal classification: `InvokeLocal` →
lower nothing (return null); `UseKernel<T>()` → lower the `Where`/`Select` stages above it;
`InvokeKernel(lambda)` → lower the terminal lambda to `Handle`.

### C2. IR shape per lowered chain
One `SandboxModule` with `ShouldHandle` (all `Where`s AND-composed, referencing the projection) and
`Handle` (the terminal `ctx.Messages.Send`). `Select` is spliced into downstream references via the
generalized `DotBoxdExpressionLoweringContext` (extend it to bind projection parameters/variables with
known types in addition to event properties + live settings). MVP: scalar projections only.

### C3. Emit + registry
Each chain → generated `internal static class <ChainId>PluginPackage` (ChainId = stable hash of the
seed's file+span) reusing `DotBoxdPackageSourceEmitter`, registered in a generated
`DotBoxdGeneratedPackages` registry by chainId. Also emit the per-kernel `[ModuleInitializer]`
self-registration into `KernelPackageRegistry` (B4 contract); registry keys must use
`PluginAttribute.Id` to match `KernelTypeMetadata.PluginId`.

### C4. Constraints + diagnostics
Lowerable subset = exactly what the lowerer accepts today. Extend the forbidden-host-API analyzer
(`DotBoxdPluginAnalyzer`, `DBXK001`) to also fire inside to-be-lowered lambdas. New diagnostics
`DBXK110`–`DBXK114` (unsupported construct in chain lambda; unsupported `Select` type; unmappable
captured variable; chain not statically resolvable → runs native, informational; `InvokeKernel`
terminal not a single `Send`). Add to `AnalyzerReleases.Unshipped.md`.

### C5. Sub-phasing (land independently, in order)
- **C-0** detection + `DBXK113` info only, zero behavior change (prove incrementality with
  `WithTrackingName` + `PluginAnalyzerIncrementalityTests`).
- **C-1** the `[ModuleInitializer]` kernel-type→package registry (already needed by B4; low risk).
- **C-2** `Where` filter lowering above `UseKernel<T>()`. *Hardest new semantic work:* mapping
  captured `LiveValue`/`LiveContext` (e.g. `serverGate.Value`) to live-setting bindings in chains
  with no `[Plugin]` class (`DBXK112` for unmappable).
- **C-3** `Select` projection + chained filters + `InvokeKernel` terminal. Verify synthesized
  manifests/effects/capabilities pass `PluginPackageValidator` + the verifier.
- **C-3.5** (optional) tuple/record projections (`DBXK111` gates until then).

**Phase C verify:** extend `tests/DotBoxd.Kernels.Tests/PluginAnalyzer/Core/PluginAnalyzerIncrementalityTests.cs`
and the `PluginAnalyzer/Generated/*` golden snapshots; add an end-to-end example chain that builds,
lowers, ships, and runs sandboxed.

---

## Critical files (by phase)

- **A:** `examples/GameServer/DotBoxd.Kernels.Game.PluginHost/**` (→ `DotBoxd.Kernels.Game.Plugin/**`),
  `examples/GameServer/DotBoxd.Kernels.Game.Server/Ipc/PluginHostLauncher.cs`,
  `examples/GameServer/DotBoxd.Kernels.Game.Server/Program.cs`, `DotBoxd.Kernels.slnx`,
  `scripts/check-docs-smoke.ps1`, `docs/Specs/Addendum/Examples.md`, `README.md`.
- **B:** `src/DotBoxd.Plugins/Runtime/HookRegistry.cs`, `src/DotBoxd.Plugins/PluginServer.cs`,
  new `src/DotBoxd.Plugins/Runtime/KernelPackageRegistry.cs`,
  new `src/DotBoxd.Plugins/Runtime/EventRegistry.cs`, `docs/api-baselines/DotBoxd.Plugins.txt`.
- **C:** `src/DotBoxd.Plugins.Analyzer/Analysis/DotBoxdPluginPackageGenerator.cs`,
  `.../DotBoxdPackageSourceEmitter.cs`,
  `.../Lowering/Expressions/DotBoxdExpressionLoweringContext.cs`,
  `.../Lowering/DotBoxdGenerationNames.cs`, `.../PluginAnalyzerDiagnostics.cs`,
  `AnalyzerReleases.Unshipped.md`, new `HookChainModelFactory.cs` / `HookChainModel.cs` /
  `DotBoxdPackageRegistryEmitter.cs`.

## Suggested delivery
Land **A** first (visible request, low risk, green CI). Then **B** (fluent API + auto-install;
delete the Phase-A `KernelPackageCatalog`). Then **C** in sub-phases C-0 → C-3. Each phase is a
separate commit/PR with its own green build + tests.

## Round-2 design (review feedback)
A second review round added five requirements on top of this plan — fluent `Where`/`Select` gating
instead of a `UseKernel(filter:)` parameter, kernel **ownership/lifecycle** (sessions, revoke on
disconnect), **inferred** event adapters, **per-plugin authentication + signing + policy**, and a
server `Program` class. Their design lives in
[ownership-auth-and-policy.md](ownership-auth-and-policy.md); the walkthroughs reflect the
API-visible parts.
