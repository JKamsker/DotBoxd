# RemotePluginServerBuilder + InvokeAsync inline-kernel — Phased Implementation Plan

## Context

The GameServer sample plugin (`samples/Kernels/GameServer/Examples.GameServer.Plugin/Program.cs`) registers
kernels imperatively today:

```csharp
var server = new RemotePluginServer(connection.Get<IGamePluginControlService>());
var guardianId    = await server.Kernels.Register<IMonsterAggroService, GuardianKernel>();
var retaliationId = await server.Kernels.Register<IAttackService, RetaliationKernel>();
var killerId      = await server.KernelRpc.Register<IMonsterKillerService, MonsterKillerKernel>();
```

The user wants two additions, **without removing the imperative APIs**:

1. **A fluent `RemotePluginServerBuilder`** (`FromConnection(...)` / `FromPipeName(...)`,
   `.SetupKernelRpc(...)`, `.SetupKernels(...)`, `.Build()`) as pure syntactic sugar over the existing
   `RemoteKernelControl.Register` / `RemoteKernelRpcControl.Register`.
2. **A new `server.Kernels.InvokeAsync(lambda)`** that lowers an anonymous block-body lambda to verified
   sandboxed IR at compile time (like the existing `InvokeKernel(lambda)` interceptor path), ships it over
   async IPC, and runs it server-side. No-capture lambdas use the lambda-only overload. Capture sync-in/out
   uses the implemented explicit mutable capture-bag overload because generated C# interceptor bodies cannot
   directly read or write caller locals by name.

### The DotBoxD invariant (preserved throughout)

The server is frozen at release and **never compiles plugin source**. Plugins ship **verified sandboxed
IR only**. Capabilities are derived by the analyzer from what the IR actually touches
(`[HostBinding]` calls), never self-asserted; they gate the install via `ServerPolicy.ForKernel` →
`SandboxHost.PrepareAsync` → `PolicyResolver.Validate` (deny-at-install). Anonymous `InvokeAsync` kernels
go through the identical install + capability-gating + session-ownership path as named RPC kernels.

### What already works (verified, do not rebuild)

- `RemoteKernelControl.Register<TService,TKernel>()` and `RemoteKernelRpcControl.Register<TService,TKernel>()`
  are fully functional `async ValueTask<string>` IPC round-trips
  (`Client/RemotePluginServer.cs:50-56, 74-82`). The latter stores `typeof(TService) → pluginId` in
  `_services`, enabling `PluginId<TService>()` (lines 84-89).
- `server.World.Monsters.KillMonstersAsync(...)` and `server.World.Monsters.MonsterKiller` already exist —
  generated purely from `[KernelRpcClientProperty]`/`[KernelRpcClientMethod]` on `MonsterKillerKernel`,
  **independent of how Register is called**. The builder does not change extension generation.
- `KernelRpcBinaryCodec` (encode/decode `KernelRpcValue` ↔ `byte[]`), `KernelRpcValueConverter`
  (`KernelRpcValue` ↔ `SandboxValue`), `InstalledKernel.InvokeRpcAsync` + `BuildRpcInput`, and the
  `InvokeKernel` interceptor pipeline (`PluginPackageGenerator.cs:37-45`,
  `DotBoxDHookChainInterceptorEmitter.cs`) are all reusable as-is or as templates.

### Phasing rationale

The builder (Phase 1) is a self-contained, low-risk runtime facade with **zero generator and zero
wire-protocol change**. `InvokeAsync` is a substantially larger investment that touches the analyzer
(a new interceptor pipeline + capture analysis + lambda lowering), the runtime wire protocol (a response
that carries mutated captures alongside the return value), and the IPC contract. It is split across
Phases 2–4 so each is independently shippable with green build + tests. The richer
`world.Monsters.Get(id).Name` object surface is its own **Phase 4** (fully planned below, not just stated).

> **Decided (user):** The builder follows the ASP.NET Core `HostBuilder` lifecycle — a **synchronous
> `Build()`** that constructs the server, then an **async `StartAsync()` / `RunAsync()`** that performs the
> connect + registration I/O. This is the `var app = builder.Build(); await app.RunAsync();` shape, and it
> resolves the "sync `.Build()` over async IPC" tension cleanly: `Build()` does no I/O, so it cannot deadlock;
> all installs happen in the async start step. Phase 1 below reflects this.

---

## Phase 1 — `RemotePluginServerBuilder` (fluent registration sugar)

**Goal.** Add a fluent builder that delegates to the existing `Register` methods. No generator change, no
wire change, no new attribute. The imperative `Program.cs` block keeps compiling and running unchanged.

### Decisions folded in from review

- **`Build()` is synchronous; `StartAsync()` / `RunAsync()` do the I/O (ASP.NET Core `HostBuilder` shape).**
  `Build()` does **no** I/O — it validates the queued setup and returns an **unstarted** `RemotePluginServer`,
  so it cannot deadlock. The connect (for `FromPipeName`) and all kernel/RPC `Register` round-trips run inside
  `StartAsync(CancellationToken)`. `RunAsync(CancellationToken)` is the convenience that does
  `StartAsync()` **then** `HoldUntilShutdownAsync()` (start → hold-until-server-completes → disconnect), exactly
  like `IHost.RunAsync` = `StartAsync` + wait-for-shutdown + `StopAsync`.
- **`Started` gate on the typed surface.** Because installs move to `StartAsync`, the server is **unstarted**
  between `Build()` and `StartAsync()`. Accessing `Kernels` / `KernelRpc` / `World` (or calling a generated
  extension such as `server.World.Monsters.KillMonstersAsync(...)`) before `StartAsync()` completes throws
  `InvalidOperationException("Call StartAsync() before using the server.")`. After `StartAsync()` returns, the
  full typed surface is live — `PluginId<TService>()` resolves because registration already populated
  `_services`. (With `FromConnection`, where the control is already in hand, `StartAsync` only flushes
  registrations; the controls may be constructed eagerly in `Build()` and the gate is a simple started-flag.)
- **Two-step vs `RunAsync`.** A plugin that does **interleaved imperative work** (the GameServer sample:
  `KillMonstersAsync`, `InvokeAsync`) between registration and shutdown uses the two-step form —
  `Build()` → `await StartAsync()` → work → `await HoldUntilShutdownAsync()`. A plugin whose only behavior is
  event kernels (no interleaved work) can collapse to `await Build().RunAsync()`. Both are supported; the
  sample demonstrates the two-step.
- **Connection ownership + `IAsyncDisposable`.** When built via `FromPipeName`, the server **owns** the
  connection it opened in `StartAsync` and disposes it on `DisposeAsync` (the canonical `await using` pattern).
  When built via `FromConnection`, the caller still owns the passed-in control and the server does **not**
  dispose it (it only wraps it) — preserving today's `RemotePluginServer` semantics.
- **Eager, in-order registration inside `StartAsync`.** All `SetupKernels` registrations flush, then all
  `SetupKernelRpc` registrations flush, **before** `StartAsync` returns. This is the correctness guarantee that
  makes post-start calls safe: `server.World.Monsters.KillMonstersAsync(...)` resolves its plugin id via
  `value.KernelRpc.PluginId<IMonsterKillerService>()`, which throws unless `RemoteKernelRpcControl.Register`
  already populated `_services`.
- **Sequential flush, not `Task.WhenAll`.** `RemoteKernelRpcControl._services` is a plain
  non-thread-safe `Dictionary`; parallel completion races its writes. Sequential awaits also avoid the
  `WhenAll` failure-masking problem. IPC install is not the bottleneck.
- **Distinct accumulator constraints.** `KernelRegistrationAccumulator.Register<TService,TKernel>()` uses
  `where TKernel : class, TService` (matches `RemoteKernelControl.Register`).
  `KernelRpcRegistrationAccumulator.Register<TService,TKernel>()` uses `where TKernel : class` **only**
  (matches `RemoteKernelRpcControl.Register`; `MonsterKillerKernel` does **not** implement
  `IMonsterKillerService`).
- **Builder placement is the dominant constraint.** Two consistent options; pick one explicitly:
  - **Option A (recommended): new shared library `src/Hosting/DotBoxD.Plugins.Client/`** holding
    `RemotePluginServerBuilder` + accumulators. The test project references it; the builder is reusable
    beyond the GameServer sample. The accumulators wrap `RemoteKernelControl`/`RemoteKernelRpcControl`,
    which are `internal sealed` in the sample — so the builder needs the controls exposed. The cleanest
    way: keep the builder in the **sample** but add a **sample-side test project**
    `samples/Kernels/GameServer/Examples.GameServer.Plugin.Tests` with `InternalsVisibleTo`.
  - **Option B: builder stays sample-local (`Client/`), tests live in a new sample-side test project.**
  The existing `tests/DotBoxD.Kernels.Tests` project has **no reference** to any `Examples.GameServer.*`
  assembly and no `InternalsVisibleTo`, so it **cannot** host builder runtime tests as-is. This must be
  resolved before writing Phase-1 runtime tests. Recommended: **Option B** (smallest blast radius; matches
  Decision-3's "keep it sample-local"), with a new `Examples.GameServer.Plugin.Tests` project.

### File-level tasks

| File | Action |
|---|---|
| `samples/Kernels/GameServer/Examples.GameServer.Plugin/Client/RemotePluginServerBuilder.cs` | **Create.** `RemotePluginServerBuilder` (private ctor; `FromConnection(IGamePluginControlService)` and `FromPipeName(string)` sync factories — both return the builder synchronously, `FromPipeName` deferring `RpcMessagePackIpc.ConnectNamedPipeAsync` to `StartAsync`); `SetupKernels(Action<KernelRegistrationAccumulator>)`; `SetupKernelRpc(Action<KernelRpcRegistrationAccumulator>)`; **`Build() : RemotePluginServer` (sync, no I/O)**. Plus the two accumulators (each collects `Func<ValueTask>` and flushes sequentially in order). |
| `samples/Kernels/GameServer/Examples.GameServer.Plugin/Client/RemotePluginServer.cs` | **Modify.** Add the lifecycle: a started-flag gate on `Kernels`/`KernelRpc`/`World`; `StartAsync(CancellationToken)` (connect-if-deferred → flush kernel then RPC registrations → mark started); `RunAsync(CancellationToken)` (= `StartAsync` + `HoldUntilShutdownAsync`); implement `IAsyncDisposable` (disposes the owned connection only when opened via `FromPipeName`). The existing public `Register`/`Get`/`World` surface is unchanged; this only adds members. |
| `samples/Kernels/GameServer/Examples.GameServer.Plugin/Program.cs` | **Modify.** Keep the imperative block intact (compiles + runs). Add the builder block as a sibling demonstration (or a second `RunWithBuilderAsync` entry selected by a `--use-builder` arg), using `Build()` → `await StartAsync()` → work → `await HoldUntilShutdownAsync()`. |
| `samples/Kernels/GameServer/Examples.GameServer.Plugin.Tests/Examples.GameServer.Plugin.Tests.csproj` | **Create.** xUnit project, `ProjectReference` to `Examples.GameServer.Plugin`; add `[assembly: InternalsVisibleTo("Examples.GameServer.Plugin.Tests")]` to the plugin (e.g. in a new `AssemblyInfo.cs` or csproj `<InternalsVisibleTo>`). |
| `DotBoxD.slnx` | **Modify.** Add the new test project under the GameServer solution folder. |

### Builder shape (sketch — single, reconciled form)

```csharp
internal sealed class RemotePluginServerBuilder
{
    private readonly Func<ValueTask<IGamePluginControlService>> _controlFactory;
    private readonly List<Func<RemoteKernelControl, ValueTask>> _kernelSetup = [];
    private readonly List<Func<RemoteKernelRpcControl, ValueTask>> _rpcSetup = [];

    private RemotePluginServerBuilder(Func<ValueTask<IGamePluginControlService>> controlFactory)
        => _controlFactory = controlFactory;

    public static RemotePluginServerBuilder FromConnection(IGamePluginControlService control)
        => new(() => ValueTask.FromResult(control));

    public static RemotePluginServerBuilder FromPipeName(string pipeName)
        => new(async () =>
        {
            var connection = await RpcMessagePackIpc.ConnectNamedPipeAsync(pipeName).ConfigureAwait(false);
            return connection.Get<IGamePluginControlService>();
        });

    public RemotePluginServerBuilder SetupKernels(Action<KernelRegistrationAccumulator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _kernelSetup.Add(async kernels =>
        {
            var acc = new KernelRegistrationAccumulator(kernels);
            configure(acc);
            await acc.FlushAsync().ConfigureAwait(false);   // sequential, in registration order
        });
        return this;
    }

    public RemotePluginServerBuilder SetupKernelRpc(Action<KernelRpcRegistrationAccumulator> configure) { /* symmetric */ }

    /// <summary>Synchronously constructs the server. Does NO I/O — the connect and all Register round-trips
    /// run in <see cref="RemotePluginServer.StartAsync"/>. The returned server is UNSTARTED; its typed
    /// surface (Kernels/KernelRpc/World) throws until StartAsync completes.</summary>
    public RemotePluginServer Build()
        => new(_controlFactory, _kernelSetup, _rpcSetup);   // captures setup; opens nothing
}

// On RemotePluginServer (added members):
//   public async ValueTask StartAsync(CancellationToken ct = default)
//   {
//       _control = await _controlFactory().ConfigureAwait(false);   // connects if FromPipeName
//       BuildControls(_control);                                    // Kernels/KernelRpc/World now live
//       foreach (var s in _kernelSetup) { ct.ThrowIfCancellationRequested(); await s(Kernels).ConfigureAwait(false); }
//       foreach (var s in _rpcSetup)   { ct.ThrowIfCancellationRequested(); await s(KernelRpc).ConfigureAwait(false); }
//       _started = true;
//   }
//   public async ValueTask RunAsync(CancellationToken ct = default)
//   {
//       if (!_started) await StartAsync(ct).ConfigureAwait(false);
//       await HoldUntilShutdownAsync().ConfigureAwait(false);       // disconnect on return
//   }
//   public async ValueTask DisposeAsync() { if (_ownsConnection) await _connection.DisposeAsync(); }
```

Both factories return the builder synchronously, so construction is a single expression and the async
lifecycle is explicit — the ASP.NET Core `var app = builder.Build(); await app.RunAsync();` shape:

```csharp
await using var server = RemotePluginServerBuilder
    .FromPipeName(pipeName)
    .SetupKernels(k => k
        .Register<IMonsterAggroService, GuardianKernel>()
        .Register<IAttackService, RetaliationKernel>())
    .SetupKernelRpc(krn => krn
        .Register<IMonsterKillerService, MonsterKillerKernel>())
    .Build();                              // sync construction, no I/O

await server.StartAsync();                 // connect + flush registrations; typed surface now live

// plugin work — generated extensions and InvokeAsync are usable here:
var killResults = await server.World.Monsters.KillMonstersAsync(["monster-3", "monster-4"]);

await server.HoldUntilShutdownAsync();     // hold until the server completes; DisposeAsync disconnects
```

A pure event-kernel plugin with no interleaved imperative work collapses the last three lines to
`await server.RunAsync();`.

### Generator / runtime / wire pieces that change

- **Generator: none.** The spec note "SetupKernelRpc has an attribute which the sourcegen listens for"
  does **not** apply to the builder. The `server.World.Monsters.*` extensions are generated from attributes
  on the kernel class regardless of call site (`RpcKernelModelFactory` drives extension emission off the
  kernel type, not the registration site). A `[SetupKernelRpc]` attribute + a fourth
  `ForAttributeWithMetadataName` pipeline would fire on the **builder method declaration body** (which
  contains no `Register<>()` calls), never the caller's lambda — it would validate nothing. **No fourth
  pipeline in Phase 1.** Any generator work belongs to `InvokeAsync` (Phase 2).
- **Runtime: none beyond the new builder/accumulator types.** They delegate to existing `Register`.
- **Wire: none.**

### Tests (Phase 1)

New project `Examples.GameServer.Plugin.Tests` with a recording fake `IGamePluginControlService`:

- `Build_performs_no_io` — after `Build()` (before `StartAsync`), the recording fake has observed **zero**
  connect/install calls.
- `Surface_throws_before_StartAsync` — touching `server.Kernels`/`World`/`KernelRpc` before `StartAsync`
  throws `InvalidOperationException`.
- `StartAsync_registers_all_kernels_before_returning` — both kernel installs are made when `StartAsync`
  returns.
- `StartAsync_registers_rpc_service_and_populates_PluginId` — after `StartAsync`,
  `server.KernelRpc.PluginId<IMonsterKillerService>()` returns the fake-reported id.
- `Registrations_flush_in_declaration_order_and_complete_before_StartAsync_returns` —
  `server.World.Monsters.KillMonstersAsync(...)` is callable immediately after `StartAsync` against the fake.
- `FromConnection_wraps_existing_control_without_opening_pipe`.
- `FromPipeName_defers_connection_until_StartAsync` — no connect call until `StartAsync` is awaited.
- `RunAsync_starts_then_holds_until_shutdown` — `RunAsync` flushes registrations then awaits
  `HoldUntilShutdownAsync`; returns when the fake signals shutdown.
- `DisposeAsync_disconnects_owned_FromPipeName_connection_only` — `FromConnection` does not dispose the
  caller-owned control.
- `Original_imperative_path_and_builder_path_produce_equivalent_calls` — both paths drive the same
  recording fake to the same observed install sequence.
- `KernelRpc_accumulator_accepts_kernel_not_implementing_service` — compile-time proof that
  `Register<IMonsterKillerService, MonsterKillerKernel>()` binds (constraint is `class` only).

### Exit criteria (Phase 1)

`dotnet build DotBoxD.slnx -c Release` green; new test project green; the GameServer E2E
(`dotnet run --project samples/Kernels/GameServer/Examples.GameServer.Server -c Release`) exits 0 on both
the imperative and `--use-builder` paths with identical output; existing
`tests/DotBoxD.Kernels.Tests` unaffected.

---

## Phase 2 — `InvokeAsync`: detection, lambda lowering, sync-in only (flat world surface)

**Goal.** Detect `server.Kernels.InvokeAsync(lambda)` at compile time, lower the **block-body** lambda to
verified IR via the existing RPC lowerer, ship it as an anonymous RPC kernel, run it server-side, and
return the typed result. **Sync-in captures only** (locals read inside the lambda). **No sync-out** and
**no object-returning world bindings** in this phase. The flat `IGameWorldAccess` scalar surface is used
exactly as `MonsterKillerKernel` already uses it.

### Decisions folded in from review

- **Detection mirrors `InvokeKernel`.** A new `CreateSyntaxProvider` keyed on
  `MemberAccessExpressionSyntax { Name.Identifier.ValueText: "InvokeAsync" }`. The semantic transform
  resolves the receiver and **skips (returns null) unless the receiver type is the kernel-invocation
  surface** (`RemoteKernelControl`), mirroring `HookChainModelFactory`'s receiver-type guard. Name alone
  is not sufficient.
- **Lambda shape.** Exactly one explicitly-typed parameter whose type is the host-access interface
  (`IGameWorldAccess`), **block body** (expression-body lambdas are out of scope; use `InvokeKernel`).
- **Capture analysis via `SemanticModel.AnalyzeDataFlow(block)`.** Sync-in = `DataFlowsIn` ∩
  enclosing-scope locals/params, minus the lambda parameter. Each capture's type must be supported by
  `DotBoxDRpcTypeMapper.JsonType`; unsupported types fail safe (no output).
- **Capture marshalling is explicit, NOT closure reflection.** Reading captured fields from
  `delegate.Target` via `GetField("name")` relies on Roslyn closure name-mangling, which is **not
  spec-guaranteed** (the design even mis-guessed `"<lastMonsterName>i__Field"`). Reflection-on-closure
  **must not ship.** A compiler probe showed generated interceptor method bodies cannot directly reference
  caller locals by name either. Resolution: lambda-only `InvokeAsync` accepts no ambient captures; sync-in/out
  is provided by an explicit mutable capture-bag overload. The bag is encoded as a record argument, and
  assigned bag properties are decoded from a response record and written back after the await.
- **Lowerer reuse is partial, stated honestly.** `DotBoxDRpcJsonLowerer.LowerBody` lowers the body
  **statements** unchanged. Net-new generator code: (a) building the IR parameter list from the sync-in
  captures, and (b) ensuring captured-local **reads** resolve to the IR parameters. The lowerer resolves
  every identifier to `Var(name)` with no rename hook (`DotBoxDRpcJsonLowerer.Expressions.cs`), so the IR
  parameters **must use the original capture names** (no `__cap_` prefix) — accepting that a kernel-local
  may not shadow a capture name. No identifier-rename pass is invented.
- **Anonymous kernel identity.** `pluginId = "$anon:" + HookChainIdentity.Compute(invocation)` (FNV-1a of
  file path + span start). Verified to pass `ValidateText` / descriptor guards. The generator emits
  `module.id == pluginId` and `module.metadata.pluginId == pluginId` identically (the existing RPC factory
  pattern). Per-connection `RemoteKernelControl` construction naturally clears the install cache on
  reconnect.
- **Concurrency-safe install is mandatory.** `EnsureAnonymousKernelAsync` uses
  `ConcurrentDictionary<string, Task<string>>` (install-once-per-id-per-connection via `GetOrAdd` of a
  `Task`). A plain check-then-install races: two concurrent first-calls double-install, and the same-owner
  reinstall guard (`KernelRegistry.Add`, DBXK060) **revokes the in-flight kernel mid-execution**.
- **`InterceptsLocationAttribute` emission must be deduplicated up front.**
  `DotBoxDHookChainInterceptorEmitter.Emit` calls `AddSource("DotBoxDInterceptsLocationAttribute.g.cs", ...)`.
  A second emitter adding the same hint name **hard-breaks the generator** whenever a compilation contains
  both a hook chain and an `InvokeAsync`. Extract a shared `InterceptsLocationAttributeEmitter.EnsureEmitted`
  driven by a combined `IncrementalValueProvider<bool>` over both interception sets; emit the attribute file
  exactly once. **Prerequisite, not optional.**
- **Null-interception is a visible diagnostic.** If `GetInterceptableLocation` returns null, emit a build
  diagnostic instead of silently leaving a throwing stub. Ensure the `InvokeAsync<TReturn>(Func<...>)`
  overload is the **sole** candidate at the call site to avoid overload-resolution mis-binding.
- **World surface = flat scalars only.** All v2-style `world.Monsters.Get(id)` / `monster.Name` references
  are removed from Phase-2 examples. `DotBoxDRpcJsonLowerer.LowerInvocation` accepts only
  `[HostBinding]`-annotated methods called directly on the lambda parameter
  (`world.GetHealth(id)`, `world.GetLevel(id)`, `world.GetPosition(id)`, `world.IsMonster(id)`,
  `world.KillMonster(id)`, `world.GetThreat(id)`). The kernel body assembles any DTO from those scalars,
  exactly as `MonsterKillerKernel` does.

### File-level tasks

| File | Action |
|---|---|
| `src/CodeGeneration/.../HookChains/InterceptsLocationAttributeEmitter.cs` | **Create.** Shared one-shot emitter for `DotBoxDInterceptsLocationAttribute.g.cs`. Refactor `DotBoxDHookChainInterceptorEmitter.Emit` to call it instead of emitting the attribute itself. |
| `src/CodeGeneration/.../InvokeAsync/InvokeAsyncModelFactory.cs` | **Create.** Detection (receiver-type guard), lambda-shape validation, `AnalyzeDataFlow` sync-in capture analysis, `DotBoxDRpcJsonLowerer.LowerBody` invocation, IR function construction (sync-in captures as leading params using original names), manifest + package JSON (mirrors `RpcKernelModelFactory.EmitPackage`: `mode=Auto`, `liveSettings=[]`, `subscriptions=[]`, `rpcEntrypoint`=function id, `requiredCapabilities` from the host-binding sink). Returns `InvokeAsyncResult(Package, Interception)`. |
| `src/CodeGeneration/.../InvokeAsync/InvokeAsyncInterceptorEmitter.cs` | **Create.** Emits the `[InterceptsLocation]` interceptor: encode sync-in captures → `EnsureAnonymousKernelAsync` → `InvokeKernelRpcAsync` → `DecodeValue` → typed result reconstruction. Emits a null-interception diagnostic when location is null. |
| `src/CodeGeneration/.../PluginPackageGenerator.cs` | **Modify.** Add the fourth `CreateSyntaxProvider` pipeline; register the package output (reuse `AddSource(package.HintName, package.Source)`); wire the combined attribute-dedup provider; register the interceptor output. |
| `src/CodeGeneration/.../Lowering/DotBoxDGenerationNames.cs` | **Modify.** Add `Metadata.KernelInvocationSurfaceType` constant (FQN of `RemoteKernelControl`). |
| `samples/Kernels/GameServer/Examples.GameServer.Plugin/Client/RemotePluginServer.cs` | **Modify `RemoteKernelControl`.** Add `InvokeAsync<TReturn>(Func<IGameWorldAccess,TReturn>)` throwing stub (replaced by the interceptor); add `internal IKernelRpcWireClient WireClient` (the `IGamePluginControlService`, which implements `InvokeKernelRpcAsync`); add `EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> factory)` with `ConcurrentDictionary<string, Task<string>>` caching, calling `_control.InstallKernelRpcAsync`. |

### Generator / runtime / wire pieces that change

- **Generator:** new pipeline + two new emitters + shared attribute emitter. Reuses `DotBoxDRpcJsonLowerer`,
  `DotBoxDHostBindingExpressionLowerer` (capability sink), `DotBoxDRpcTypeMapper`, `HookChainIdentity`.
- **Runtime:** new `RemoteKernelControl` members only. **No new IPC method this phase** — sync-in uses the
  existing `InstallKernelRpcAsync` + `InvokeKernelRpcAsync(pluginId, byte[]) → byte[]` (the response is the
  bare return value, no envelope needed yet).
- **Wire:** unchanged. `EncodeArguments(syncInCaptures)` → existing `InvokeKernelRpcAsync` →
  `DecodeValue(returnValue)`.

### Tests (Phase 2)

Generator tests (in `tests/DotBoxD.Kernels.Tests/Plugins/Rpc/`, the existing extension-test folder, using
self-contained string fixtures with inline stub `RemoteKernelControl`/`IGameWorldAccess`):

- `InvokeAsync_block_body_lambda_generates_interceptor_and_package`.
- `InvokeAsync_capture_read_adds_leading_ir_parameter` — IR function has `1 + captureCount` params.
- `InvokeAsync_unsupported_capture_type_emits_no_output` — fails safe.
- `InvokeAsync_expression_body_lambda_is_ignored` (use `InvokeKernel` instead).
- `InvokeAsync_null_interceptable_location_emits_diagnostic`.
- `InterceptsLocationAttribute_emitted_once_when_both_hookchain_and_invokeasync_present` — the dedup guard.

Runtime/round-trip tests (the anonymous package validated + executed via `PluginServer.Create` +
`InstalledKernel.InvokeRpcAsync` with hand-built IR matching `BuildRpcInput`'s 0/1/N-param shapes):

- `Anonymous_kernel_install_validates_and_prepares` — `RpcKernelPackageValidator.Validate` /
  `ValidatePrepared` accept the `$anon:` package (rpcEntrypoint set, return type known).
- `Anonymous_kernel_capability_gating_derived_from_lambda_body` — required capabilities equal exactly the
  host-binding set; install fails when a grant is missing.
- `Invoke_single_capture_passes_bare_value` and `Invoke_multi_mixed_type_captures_round_trip` — exercise the
  1-param bare path and the N-param `FromList(values, values[0].Type)` heterogeneous frame.
- `Concurrent_first_invokes_install_once` — `EnsureAnonymousKernelAsync` does not double-install.

### Exit criteria (Phase 2)

Build green; a GameServer sample `InvokeAsync` call that reads a captured `monsterId` local and returns a
`MonsterDto[]` assembled from flat bindings compiles, lowers, installs, runs sandboxed, and returns the
correct value end-to-end; capability gating denies a lambda that touches an ungranted binding.

---

## Phase 3 — `InvokeAsync` sync-out (closure write-back via response envelope)

**Goal.** Support locals **assigned** inside the lambda being written back into the caller's closure after
the await. This requires (a) lowering write-back captures, (b) a response that carries mutated captures
alongside the return value, (c) the interceptor writing them back.

### Decisions folded in from review

- **Sync-out cannot widen the interceptor signature.** A C# interceptor's non-receiver parameters must
  match the intercepted method exactly; it cannot add `out`/`ref`. Write-back therefore happens **inside
  the generated interceptor body** (which replaces the call expression in the caller's method, where the
  caller's locals are in scope), assigning the decoded sync-out values back to those locals. No
  closure reflection.
- **Sync-out captures = `WrittenInside` ∩ enclosing-scope locals, minus lambda-declared locals.** A
  capture that is both read and written is a sync-in **parameter** that the IR body reassigns via `set`
  (the interpreter shares one slot space for params + locals, so reassigning a parameter is legal and is
  read back at return). A **write-only** capture (assigned, never read inside) has no leading param and the
  generator must declare an IR local slot for it.
- **Response envelope, not `PluginManifest.Metadata`.** `PluginManifest` has **no** `Metadata` member
  (verified). Two viable carriers for the per-kernel `syncOutCount`:
  - **Module metadata** — `module.metadata` is an open string→string dictionary the JSON importer
    enumerates without a key whitelist (verified: `JsonImporter.ReadMetadata`). Emit
    `"$anon.syncOutCount":"N"` and read via `kernel.Package.Module.Metadata["$anon.syncOutCount"]`.
  - **Field-count inference** — the entrypoint returns `Record([syncOut0,…,syncOutK-1, returnValue])`;
    `syncOutCount = ((RecordValue)result).Fields.Count - 1`. **Recommended** (no importer dependency), with
    an install-time shape validator (new diagnostic `DBXK073`) asserting the IR return type is a Record
    whose arity ≥ 1.
- **Return-wrapping must be structural, not string substitution.** Scanning the lowered JSON for
  `{"op":"return","value":` is unsound: bodies have **multiple** returns (inside `if`/`else`, nested
  blocks). The `record.new([captures…, userReturn])` wrapping is emitted by the **new factory** at every
  return site, by lowering each return statement's value through the lowerer and wrapping it — i.e. the
  factory drives `DotBoxDRpcJsonLowerer` per return-statement and synthesizes the record, rather than
  post-processing the body string. (If a single-return constraint is acceptable for v1 sync-out, the
  factory may reject multi-return lambdas with a clear diagnostic instead — see Risks.)
- **New IPC method `InvokeAnonymousKernelRpcAsync`** because the response now carries an envelope. Add a
  small `AnonResponseCodec` (`[varint syncOutCount][syncOut values…][returnValue]`) reusing the codec
  primitives — which requires making `KernelRpcBinaryCodec.WriteLength`/`WriteValue`/`Reader` **internal**
  (they are currently private). `AnonResponseCodec.Decode(ReadOnlyMemory<byte>)` drives the `ref struct
  Reader` by ref and **must call `EnsureConsumed()`** to preserve the existing trailing-byte tamper guard.
  Alternative that avoids any IPC contract change: encode the envelope **as a single `Record` value**
  through the existing `InvokeKernelRpcAsync` (the server already returns one `KernelRpcValue`; a
  `Record(returnValue, syncOut0, …)` fits). **Recommended:** the single-Record-over-existing-IPC approach
  for minimal surface; promote to a dedicated `AnonResponseCodec` + IPC method only if a non-record return
  type makes the wrapper ambiguous.
- **Nullable-reference captures are rejected in v1 sync-out (and sync-in).** `KernelRpcValue.String(null)`
  coerces to empty, and `KernelRpcValueConverter.ToSandboxValue` validates each value's kind against the
  IR-declared `SandboxType`; a `string` IR parameter cannot receive a `Unit`-kind value. So a
  `string? lastMonsterName` capture cannot be both String-typed and null-tolerant. **v1 emits a clear
  diagnostic for nullable-reference-type captures**; the spec example must use a non-nullable `string`
  (`firstMonsterName`) for the captured value, or accept that null becomes empty-string with a documented
  caveat.

### File-level tasks

| File | Action |
|---|---|
| `src/CodeGeneration/.../InvokeAsync/InvokeAsyncModelFactory.cs` | **Modify.** Add sync-out capture classification; declare IR slots for write-only captures; lower each return to wrap `record.new([captures…, userReturn])`; reject nullable-reference captures and (if chosen) multi-return lambdas with diagnostics. |
| `src/CodeGeneration/.../InvokeAsync/InvokeAsyncInterceptorEmitter.cs` | **Modify.** After the await, decode the response Record and assign each sync-out field back to the caller's local. |
| `src/Hosting/DotBoxD.Plugins/Runtime/Rpc/RpcKernelPackageValidator.cs` (or new `AnonymousKernelPackageValidator.cs`) | **Modify/Create.** `DBXK073`: anonymous entrypoint return type must be a Record with arity == syncOutCount + 1. |
| `src/Hosting/DotBoxD.Plugins/Runtime/Rpc/KernelRpcBinaryCodec.cs` | **Modify (only if the dedicated-codec path is chosen).** Make `WriteLength`/`WriteValue`/`Reader` internal. |
| `src/Hosting/DotBoxD.Plugins/Runtime/Rpc/AnonResponseCodec.cs` | **Create (only if the dedicated-codec path is chosen).** Encode/decode envelope; `Decode` calls `EnsureConsumed`. |
| `samples/.../Server.Abstractions/Ipc/IGamePluginControlService.cs` + `samples/.../Server/Ipc/GamePluginControlService.cs` | **Modify (only if the dedicated-codec path is chosen).** Add `InvokeAnonymousKernelRpcAsync(pluginId, byte[]) → byte[]` that reads `syncOutCount` from module metadata or infers it, splits the result Record into `(syncOut[], returnValue)`, and returns the envelope. |

### Tests (Phase 3)

- `Invoke_with_write_capture_writes_back_to_local_after_await`.
- `Invoke_read_and_write_capture_round_trips_via_set_and_return_record`.
- `Write_only_capture_gets_dedicated_ir_slot`.
- `Nullable_reference_capture_emits_diagnostic`.
- `Multi_return_lambda_wraps_every_return` (or `…emits_diagnostic` if the single-return constraint is taken).
- `Response_record_shape_mismatch_fails_install_DBXK073`.
- `AnonResponseCodec_round_trips_and_rejects_trailing_bytes` (if the dedicated codec is used).

### Exit criteria (Phase 3)

The full spec example — `lastMonsterName` synced in, reassigned inside the lambda, synced out, and
`MonsterDto[]` returned — works end-to-end with non-nullable captures, with capability gating intact.

---

## Phase 4 — richer object-returning world surface (sequenced last, fully planned)

**Goal.** Support the literal spec surface: `world.Monsters.Get(id)` returns an object whose
`.Name/.Id/.Health/.Level` the lambda reads, instead of the kernel assembling a DTO from flat scalar bindings.
This is sequenced last because it is the only piece that touches the **host-binding type system** (object/
record-returning bindings + member-access lowering on a binding result) — Phases 1–3 deliberately avoid it so
they ship on the existing scalar surface. It is a real planned phase, not a vague follow-up; it just depends
on Phases 2–3 landing first.

### Why it is the heaviest piece (verified)

`DotBoxDHostBindingExpressionLowerer` accepts only scalar binding return types today — a non-scalar return
flows through `DotBoxDTypeNameReader.SandboxTypeName` and comes back `"unsupported"`, failing the lower safe.
So the object surface is not a config change; it requires teaching the lowerer to emit a **Record-typed**
binding call and to route subsequent member access through the existing `record.get(index)` intrinsic. The IR
foundation already exists (`SandboxType.Record` / `RecordValue` / `record.get`, added for `[KernelRpcService]`
DTOs — see `docs/design/plugin-fluent-hooks-api/followups.md` §2), and `DotBoxDRpcJsonLowerer.LowerMemberAccess`
already emits `record.get`; the gap is purely on the **host-binding** side (binding result typed as a Record)
plus the matching server-side binding descriptor.

### Decisions / design

- **Field order is the contract.** `MonsterSnapshot` is a **positional** record; its member→field mapping is
  by declaration order (the same rule `[KernelRpcService]` DTOs already use). The host binding descriptor's
  `SandboxType.Record([...])` field order must match the snapshot's member order exactly, validated at
  registration.
- **`Get` returns a value snapshot, not a live handle.** The binding returns an immutable record captured at
  call time (no aliasing into live world state from inside the sandbox), consistent with the read-only,
  fuel-metered execution model. Mutating bindings (e.g. `KillMonster`) stay separate scalar bindings.
- **Member access only — no method calls on the snapshot.** `monster.Name` lowers to `record.get`;
  `monster.DoSomething()` is rejected (fail-safe), keeping the lowerer surface bounded.
- **Capability identity unchanged.** `GetMonster` carries its own `[HostBinding]` capability
  (e.g. `game.world.monster.read.snapshot`); reading the snapshot contributes exactly that capability, gated
  at install like every other binding. No new policy machinery.

### File-level tasks

| File | Action |
|---|---|
| `samples/.../Server.Abstractions/IGameWorldAccess.cs` | **Modify.** Add `MonsterSnapshot GetMonster(string entityId)` (positional record `MonsterSnapshot(string Id, string Name, int Health, int Level, int Position)`) with `[HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]`. |
| `samples/.../Server/Simulation/GameWorldHost.cs` (binding registration) | **Modify.** Register the matching binding with `ReturnType = SandboxType.Record([String,String,Int,Int,Int])` backed by the live `GameWorld`/`GameEntity` lookup. `BindingRegistryValidator` already accepts Records recursively — not a blocker. |
| `src/CodeGeneration/.../Lowering/DotBoxDTypeNameReader.cs` | **Modify.** Map the `MonsterSnapshot` symbol to its `SandboxType.Record([...])` (instead of `"unsupported"`), so host-binding return-type resolution succeeds. |
| `src/CodeGeneration/.../Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs` | **Modify.** Accept a Record binding return type; emit the binding `CallExpression` typed as the Record; allow a subsequent `MemberAccessExpressionSyntax` on the result to lower via `record.get(fieldIndex)` (reuse `DotBoxDRpcJsonLowerer.LowerMemberAccess`, resolving the member→index from the snapshot's declaration order). |
| `samples/.../Plugin/Client/RemotePluginServer.cs` (`RemoteMonsterControl`) | **Modify (optional).** Add an ordinary IPC `GetMonsterAsync(id)` only if non-sandbox plugin code (outside `InvokeAsync`) also needs the snapshot. Not required for the `InvokeAsync` lambda path. |
| `samples/.../Plugin/Program.cs` | **Modify.** Add the literal-spec `InvokeAsync` example using `world.Monsters.Get(id).Name` to demonstrate the object surface end-to-end. |

> The `world.Monsters.Get(id)` spelling (a `.Monsters.Get` sub-object) vs a flat `world.GetMonster(id)`:
> the binding is flat (`host.world.getMonster`); the `world.Monsters.Get(...)` shape would require the lowerer
> to also recognize a `.Monsters` grouping property on the lambda parameter. Recommended: expose it as flat
> `world.GetMonster(id)` to match every other binding, OR add a thin analyzer-recognized `Monsters.Get`
> grouping alias if the nested spelling is wanted — decide at Phase 4 start.

### Tests (Phase 4)

- `Object_binding_get_monster_lowers_to_record_typed_call`.
- `Member_access_on_snapshot_lowers_to_record_get_by_declaration_order`.
- `Snapshot_field_order_mismatch_fails_registration`.
- `Method_call_on_snapshot_fails_safe` (no package emitted).
- `Snapshot_read_contributes_only_its_own_capability` (install deny without the grant).
- End-to-end: an `InvokeAsync` lambda using `world.Monsters.Get("monster-3").Health` returns the live value.

### Exit criteria (Phase 4)

The literal spec example — `world.Monsters.Get(id)` with `.Name/.Id/.Health/.Level` reads inside the
`InvokeAsync` lambda — compiles, lowers (Record binding + `record.get` member access), installs under the
snapshot capability, runs sandboxed, and returns the correct `MonsterDto[]`. The flat-surface path from
Phases 2–3 still works unchanged.

### v1 fallback (Phases 2–3, until Phase 4 lands)

Keep the flat surface and let the lambda assemble any DTO from
`world.GetHealth/GetLevel/GetPosition(...)`, exactly as `MonsterKillerKernel` already does — **no** binding,
lowerer, or IPC change. This fully covers the data-fetching use case; Phase 4 is the ergonomics upgrade to the
literal object spelling.

---

## Backward compatibility

**No public or internal API is removed in any phase.** The contract is additive throughout.

- **Imperative registration is untouched.** `new RemotePluginServer(control)` +
  `server.Kernels.Register<…>()` + `server.KernelRpc.Register<…>()` compile and run exactly as today
  (`Client/RemotePluginServer.cs` is unchanged in Phase 1; Phase 2 only **adds** members to
  `RemoteKernelControl`). The existing `Program.cs` block is preserved as the imperative demonstration.
- **`server.World.Monsters.KillMonstersAsync(...)` and `.MonsterKiller`** keep working — they are generated
  from kernel-class attributes, independent of the builder. The builder does not change extension
  generation, and Phase 2 does not touch the RPC extension pipeline.
- **`Build()` is synchronous (ASP.NET Core `HostBuilder` shape).** The literal `.Build()` from the spec is
  honored: `Build()` is a pure, I/O-free construction step returning the server. The async work it cannot do
  synchronously — connect + install round-trips — lives in `StartAsync()` / `RunAsync()`, mirroring
  `var app = builder.Build(); await app.RunAsync();`. There is no blocking `.GetAwaiter().GetResult()` and
  therefore no deadlock risk. The only behavioral note: the typed surface is gated until `StartAsync()`
  completes (documented above).
- **`HoldUntilShutdownAsync` lifetime model is preserved.** With `FromPipeName`, kernels stay owned only
  while the session is alive. The canonical sequence is unchanged in meaning: construct → start (connect +
  register) → use → `await server.HoldUntilShutdownAsync()` → disconnect — now spelled `Build()` →
  `StartAsync()` → … → `HoldUntilShutdownAsync()`, or collapsed to `RunAsync()`. `DisposeAsync` only closes a
  connection the server itself opened (`FromPipeName`); a `FromConnection` caller-owned control is never
  disposed by the server, so it cannot close the session before `HoldUntilShutdownAsync` returns.
- **Anonymous `InvokeAsync` kernels reuse the existing ownership + revocation path.** They install through
  `InstallKernelRpcAsync`, are added to the session's `_owned` set, and are revoked on disconnect like any
  named kernel. No new lifecycle.
- **Wire/IPC compatibility.** Phase 1 and Phase 2 add **no** wire or IPC contract change. Phase 3 adds at
  most one **additive** IPC method (or zero, if the single-Record-over-existing-IPC option is taken); the
  existing `InvokeKernelRpcAsync` and `InstallKernelRpcAsync` are unchanged.

---

## Risks and limits

- **[HIGH→resolved with fallback] Interceptor closure-local capture marshalling is infeasible.** Generated
  interceptor methods cannot read or write caller locals by name, and closure-`Target` reflection remains
  rejected because compiler name-mangling is not a contract. The implemented fallback is an explicit mutable
  capture-bag overload on `InvokeAsync`, which is more verbose but reflection-free and compiler-stable.
- **[HIGH→resolved] `InterceptsLocationAttribute` hint-name collision.** A compilation with both a hook
  chain and an `InvokeAsync` would crash the generator on a duplicate `AddSource`. Resolved by the shared
  one-shot emitter (Phase 2 prerequisite).
- **[HIGH→resolved] Concurrent anonymous-kernel install race.** Without serialization, a second same-owner
  install revokes the in-flight kernel mid-execution. Resolved by `ConcurrentDictionary<string,Task<string>>`
  install-once-per-id.
- **[MEDIUM] Multi-return sync-out wrapping.** Wrapping `record.new` at every return site is the correct,
  structural approach; the simpler single-return constraint (with a diagnostic) is an acceptable v1
  reduction if schedule-bound. Either is sound; **string substitution on the lowered JSON is not** and is
  excluded.
- **[MEDIUM] `syncOutCount` storage.** `PluginManifest.Metadata` does not exist. Use field-count inference
  + an install-time shape validator (recommended) or `module.metadata` (verified open dictionary). Do not
  invent a manifest field.
- **[MEDIUM] Nullable-reference captures.** Rejected in v1 because the IR scalar type system cannot model
  "String-or-null." The spec's `string? lastMonsterName` must use a non-nullable capture or accept
  null→empty with a documented caveat.
- **[MEDIUM] Builder testability.** `tests/DotBoxD.Kernels.Tests` cannot see sample-internal types; a new
  sample-side test project with `InternalsVisibleTo` is required (Phase 1). Do not claim the existing test
  harness is reused unchanged for builder runtime tests.
- **[LOW] Heterogeneous multi-capture sync-in.** Works because `KernelRpcValueConverter.ToSandboxValue`
  validates each wire arg against its own IR parameter type; `BuildRpcInput`'s
  `FromList(values, values[0].Type)` element-type tag is an internal positional-frame detail. Covered by a
  test.
- **[MEDIUM] Object-returning world surface is the only host-binding-type-system change (Phase 4).** It is
  fully planned (above) but sequenced last because it is the heaviest, highest-risk piece: the host-binding
  lowerer accepts only scalar returns today. Phases 1–3 ship on the flat scalar surface; Phase 4 is the
  ergonomics upgrade to the literal `world.Monsters.Get(id).Name` spelling.

---

## Critical files (by phase)

- **Phase 1:** `samples/.../Examples.GameServer.Plugin/Client/RemotePluginServerBuilder.cs` (new),
  `samples/.../Examples.GameServer.Plugin/Program.cs`, `samples/.../Examples.GameServer.Plugin.Tests/**`
  (new), `DotBoxD.slnx`.
- **Phase 2:** `src/CodeGeneration/.../PluginPackageGenerator.cs`,
  `src/CodeGeneration/.../HookChains/InterceptsLocationAttributeEmitter.cs` (new),
  `src/CodeGeneration/.../InvokeAsync/InvokeAsyncModelFactory.cs` (new),
  `src/CodeGeneration/.../InvokeAsync/InvokeAsyncInterceptorEmitter.cs` (new),
  `src/CodeGeneration/.../Lowering/DotBoxDGenerationNames.cs`,
  `samples/.../Examples.GameServer.Plugin/Client/RemotePluginServer.cs` (`RemoteKernelControl`).
- **Phase 3:** `src/CodeGeneration/.../InvokeAsync/InvokeAsyncModelFactory.cs`,
  `…/InvokeAsyncInterceptorEmitter.cs`,
  `src/Hosting/DotBoxD.Plugins/Runtime/Rpc/RpcKernelPackageValidator.cs` (or new
  `AnonymousKernelPackageValidator.cs`), and — only if the dedicated-codec path is chosen —
  `KernelRpcBinaryCodec.cs`, `AnonResponseCodec.cs` (new), `IGamePluginControlService.cs` +
  `GamePluginControlService.cs`.
- **Phase 4 (deferred):** `samples/.../Server.Abstractions/IGameWorldAccess.cs`,
  `samples/.../Server/Simulation/GameWorldHost.cs`,
  `src/CodeGeneration/.../Lowering/Expressions/DotBoxDHostBindingExpressionLowerer.cs`,
  `src/CodeGeneration/.../Lowering/DotBoxDTypeNameReader.cs`.

## Docs

Following `docs/design/plugin-fluent-hooks-api/`, new docs live under
`docs/design/remote-plugin-server-builder/`:
- `plan.md` — this document.
- `invoke-async.md` — the InvokeAsync closure-capture inline-kernel deep dive (detection, lowering,
  capture marshalling, wire envelope, capability gating, identity).

## Suggested delivery

Land **Phase 1** first (visible request, zero generator/wire risk, green CI). Then **Phase 2** (detection +
lowering + sync-in, with the attribute-dedup and concurrency-safe install as hard prerequisites). Then
**Phase 3** (sync-out). Then **Phase 4** (object-returning world surface — the literal `world.Monsters.Get(id)`
spelling), sequenced last because it is the only host-binding-type-system change. Each phase is a separate
commit/PR with its own green build + tests.
