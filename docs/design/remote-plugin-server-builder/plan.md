# RemotePluginServerBuilder + InvokeAsync inline-kernel — Phased Implementation Plan

> **Historical design note:** this plan is superseded by
> [interface-driven-plugin-server.md](interface-driven-plugin-server.md) for the current generated server
> surface. It intentionally preserves older pre-rename API names as design history; use the interface-driven
> document and the GameServer sample for current server-extension APIs.

> **Note (2026-06-16):** the hand-written-control approach below is partly superseded by
> [interface-driven-plugin-server.md](interface-driven-plugin-server.md), which declares the surface as
> interfaces and source-generates the controls/facade/builder, unifying the world-access and remote-control
> surfaces into one `IGameWorldAccess`. The lifecycle decisions here (sync `Build()`, async `StartAsync()`)
> and the `InvokeAsync` machinery still hold.

## Context

The GameServer sample plugin (`samples/GameServer/Examples.GameServer.Plugin/Program.cs`) registers
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
2. **A new top-level `server.InvokeAsync(lambda)`** that lowers an anonymous block-body lambda to verified
   sandboxed IR at compile time (like the existing `InvokeKernel(lambda)` interceptor path), ships it over
   async IPC, and runs it server-side. No-capture lambdas and reflection-backed implicit local/parameter
   captures use the lambda-only overload; the DotBoxD Fody weaver can rewrite safe implicit captures to
   static closure-field IL. Capture sync-in/out also has the implemented explicit mutable capture-bag
   overload for compiler-stable marshalling.

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
  (`KernelRpcValue` ↔ `SandboxValue`), `InstalledKernel.InvokeServerExtensionAsync` + `BuildRpcInput`, and the
  `InvokeKernel` interceptor pipeline (`PluginPackageGenerator.cs:37-45`,
  `DotBoxDHookChainInterceptorEmitter.cs`) are all reusable as-is or as templates.

### Phasing rationale

The builder (Phase 1) is a self-contained, low-risk runtime facade with **zero generator and zero
wire-protocol change**. `InvokeAsync` is a substantially larger investment that touches the analyzer
(a new interceptor pipeline + capture analysis + lambda lowering) and the runtime invocation path
(argument encoding plus an optional response record for capture-bag sync-out). It keeps the existing
server-extension IPC contract. It is split across Phases 2–4 so each is independently shippable with
green build + tests. The richer flat snapshot surface `world.GetMonster(id).Name` is its own **Phase 4**.

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
    `samples/GameServer/Examples.GameServer.Plugin.Tests` with `InternalsVisibleTo`.
  - **Option B: builder stays sample-local (`Client/`), tests live in a new sample-side test project.**
  The existing `tests/DotBoxD.Kernels.Tests` project has **no reference** to any `Examples.GameServer.*`
  assembly and no `InternalsVisibleTo`, so it **cannot** host builder runtime tests as-is. This must be
  resolved before writing Phase-1 runtime tests. Recommended: **Option B** (smallest blast radius; matches
  Decision-3's "keep it sample-local"), with a new `Examples.GameServer.Plugin.Tests` project.

### File-level tasks

| File | Action |
|---|---|
| `samples/GameServer/Examples.GameServer.Plugin/Client/RemotePluginServerBuilder.cs` | **Create.** `RemotePluginServerBuilder` (private ctor; `FromConnection(IGamePluginControlService)` and `FromPipeName(string)` sync factories — both return the builder synchronously, `FromPipeName` deferring `RpcMessagePackIpc.ConnectNamedPipeAsync` to `StartAsync`); `SetupKernels(Action<KernelRegistrationAccumulator>)`; `SetupKernelRpc(Action<KernelRpcRegistrationAccumulator>)`; **`Build() : RemotePluginServer` (sync, no I/O)**. Plus the two accumulators (each collects `Func<ValueTask>` and flushes sequentially in order). |
| `samples/GameServer/Examples.GameServer.Plugin/Client/RemotePluginServer.cs` | **Modify.** Add the lifecycle: a started-flag gate on `Kernels`/`KernelRpc`/`World`; `StartAsync(CancellationToken)` (connect-if-deferred → flush kernel then RPC registrations → mark started); `RunAsync(CancellationToken)` (= `StartAsync` + `HoldUntilShutdownAsync`); implement `IAsyncDisposable` (disposes the owned connection only when opened via `FromPipeName`). The existing public `Register`/`Get`/`World` surface is unchanged; this only adds members. |
| `samples/GameServer/Examples.GameServer.Plugin/Program.cs` | **Modify.** Keep the imperative block intact (compiles + runs). Add the builder block as a sibling demonstration (or a second `RunWithBuilderAsync` entry selected by a `--use-builder` arg), using `Build()` → `await StartAsync()` → work → `await HoldUntilShutdownAsync()`. |
| `samples/GameServer/Examples.GameServer.Plugin.Tests/Examples.GameServer.Plugin.Tests.csproj` | **Create.** xUnit project, `ProjectReference` to `Examples.GameServer.Plugin`; add `[assembly: InternalsVisibleTo("Examples.GameServer.Plugin.Tests")]` to the plugin (e.g. in a new `AssemblyInfo.cs` or csproj `<InternalsVisibleTo>`). |
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
(`dotnet run --project samples/GameServer/Examples.GameServer.Server -c Release`) exits 0 on both
the imperative and `--use-builder` paths with identical output; existing
`tests/DotBoxD.Kernels.Tests` unaffected.

---

## Phase 2 — `InvokeAsync`: detection, lambda lowering, and implicit capture convenience

**Goal.** Detect `server.InvokeAsync(lambda)` on the generated plugin server facade at compile time, lower the **block-body** lambda to
verified IR via the existing RPC lowerer, ship it as an anonymous RPC kernel, run it server-side, and
return the typed result. Lambda-only calls support no-capture lowering and a reflection-backed implicit
local/parameter capture convenience path. Capture sync-in/out is also provided by the explicit mutable
capture-bag overload because generated C# interceptors cannot directly access caller locals.

### Decisions folded in from review

- **Detection mirrors `InvokeKernel`.** A new `CreateSyntaxProvider` keyed on
  `MemberAccessExpressionSyntax { Name.Identifier.ValueText: "InvokeAsync" }`. The semantic transform
  resolves the receiver and **skips (returns null) unless the receiver type is a generated plugin server
  facade or generated server interface**. Calls through erased `IPluginServer<TWorld>` are diagnosed because
  the interceptor needs the generated receiver type. Name alone is not sufficient.
- **Lambda shape.** Exactly one explicitly-typed parameter whose type is the host-access interface
  (`IGameWorldAccess`), **block body** (expression-body lambdas are out of scope; use `InvokeKernel`).
- **Capture analysis via `SemanticModel.AnalyzeDataFlow(block)`.** Lambda-only calls use zero arguments when
  there are no ambient captures. Captured locals/parameters become IR parameters and are read from
  `lambda.Target` by source symbol name; written captured locals/parameters are returned in the response
  record and reflected back into the closure object.
- **Capture marshalling has two modes.** A compiler probe showed generated interceptor method bodies cannot
  directly reference caller locals by name. The stable mode is the explicit mutable capture-bag overload: the
  bag is encoded as a record argument, and assigned bag properties are decoded from a response record and
  written back after the await. The convenience mode is lambda-only implicit capture: the generated
  interceptor uses `lambda.Target` reflection and source symbol names to read/write compiler closure fields.
  `DotBoxD.Plugins.Fody` can post-process the compiled assembly and replace those helper calls with direct
  display-class `ldfld`/`stfld` access when the closure type is provable. If the compiler shape does not
  expose the expected field, the reflection fallback remains and the caller can switch to the bag.
- **Lowerer reuse is partial, stated honestly.** `DotBoxDRpcJsonLowerer.LowerBody` lowers the body
  **statements** unchanged. Phase 2 builds either a zero-argument IR function or an IR function whose
  parameters are the implicit captured locals/parameters by source symbol name. Phase 3 adds the capture-bag
  record parameter, generated sync-out locals, and the assignment override for bag-property writes. No
  identifier-rename pass is invented.
- **Anonymous kernel identity.** `pluginId = "$anon:" + HookChainIdentity.Compute(invocation)` (FNV-1a of
  file path + span start). Verified to pass `ValidateText` / descriptor guards. The generator emits
  `module.id == pluginId` and `module.metadata.pluginId == pluginId` identically (the existing RPC factory
  pattern). Per-connection generated server facade construction naturally clears the install cache on
  reconnect.
- **Concurrency-safe install is mandatory.** `EnsureAnonymousKernelAsync` uses
  `ConcurrentDictionary<string, Lazy<Task<string>>>` (install-once-per-id-per-connection via `GetOrAdd` of a
  lazy task). A plain check-then-install races: two concurrent first-calls double-install, and the same-owner
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
- **World surface.** Object snapshots use the flat `world.GetMonster(id)` host binding. Member access such
  as `monster.Health` lowers through `record.get` by `MonsterSnapshot` declaration order.

### File-level tasks

| File | Action |
|---|---|
| `src/CodeGeneration/.../HookChains/InterceptsLocationAttributeEmitter.cs` | **Create.** Shared one-shot emitter for `DotBoxDInterceptsLocationAttribute.g.cs`. Refactor `DotBoxDHookChainInterceptorEmitter.Emit` to call it instead of emitting the attribute itself. |
| `src/CodeGeneration/.../InvokeAsync/InvokeAsyncModelFactory.cs` | **Create.** Detection (receiver-type guard), lambda-shape validation, implicit-capture analysis, `DotBoxDRpcJsonLowerer.LowerBody` invocation, IR function construction, manifest + package JSON (mirrors `RpcKernelModelFactory.EmitPackage`: `mode=Auto`, `liveSettings=[]`, `subscriptions=[]`, `rpcEntrypoint`=function id, `requiredCapabilities` from the host-binding sink). Returns `InvokeAsyncResult(Package, Interception)`. |
| `src/CodeGeneration/.../InvokeAsync/InvokeAsyncInterceptorEmitter.cs` | **Create.** Emits the `[InterceptsLocation]` interceptor: encode arguments → `EnsureAnonymousKernelAsync` → `InvokeServerExtensionAsync` → `DecodeValue` → typed result reconstruction. Emits reflection helpers for implicit captures and a null-interception diagnostic when location is null. |
| `src/CodeGeneration/.../PluginPackageGenerator.cs` | **Modify.** Add the fourth `CreateSyntaxProvider` pipeline; register the package output (reuse `AddSource(package.HintName, package.Source)`); wire the combined attribute-dedup provider; register the interceptor output. |
| `src/CodeGeneration/.../InvokeAsync/InvokeAsyncReceiverResolver.cs` | **Create.** Resolve generated plugin server facades, generated server interfaces, and generated builder locals to the world/access types required by the interceptor. |
| `src/CodeGeneration/DotBoxD.Plugins.Fody/**` | **Create.** Fody add-in that discovers generated `InvokeAsync_*` call sites, maps each interceptor to its compiler display class, rewrites safe reflection helper calls to static closure-field IL, and leaves reflection fallback calls untouched otherwise. |
| generated plugin server facade | **Modify.** Add top-level `InvokeAsync<TReturn>(Func<TWorld,ValueTask<TReturn>>)` throwing stub (replaced by the interceptor), capture-bag overload, `Services.WireClient` for `InvokeServerExtensionAsync`, and `Services.EnsureAnonymousKernelAsync(string pluginId, Func<PluginPackage> factory)` with `ConcurrentDictionary<string, Lazy<Task<string>>>` caching through `InstallServerExtensionAsync`. |

### Generator / runtime / wire pieces that change

- **Generator:** new pipeline + two new emitters + shared attribute emitter. Reuses `DotBoxDRpcJsonLowerer`,
  `DotBoxDHostBindingExpressionLowerer` (capability sink), `DotBoxDRpcTypeMapper`, `HookChainIdentity`.
- **Runtime:** new generated facade members only. **No new IPC method this phase** — no-capture and
  implicit-capture calls use the existing `InstallServerExtensionAsync` + `InvokeServerExtensionAsync(pluginId, byte[]) →
  byte[]` path.
- **Wire:** unchanged. `EncodeArguments(captures-or-empty)` → existing `InvokeServerExtensionAsync` →
  `DecodeValue(returnValue-or-response-record)`.

### Tests (Phase 2)

Generator tests (in `tests/DotBoxD.Kernels.Tests/Plugins/Rpc/`, the existing extension-test folder, using
self-contained string fixtures with inline generated-server facade/`IGameWorldAccess` stubs):

- `InvokeAsync_block_body_lambda_generates_interceptor_and_package`.
- `InvokeAsync_no_capture_block_body_generates_zero_argument_package`.
- `Implicit_capture_generates_reflection_arguments_and_sync_out`.
- `InvokeAsync_expression_body_lambda_is_ignored` (use `InvokeKernel` instead).
- `InvokeAsync_null_interceptable_location_emits_diagnostic`.
- `InterceptsLocationAttribute_emitted_once_when_both_hookchain_and_invokeasync_present` — the dedup guard.

Runtime/round-trip tests (the anonymous package validated + executed via `PluginServer.Create` +
`InstalledKernel.InvokeServerExtensionAsync` with hand-built IR matching `BuildRpcInput`'s 0/1/N-param shapes):

- `Anonymous_kernel_install_validates_and_prepares` — `RpcKernelPackageValidator.Validate` /
  `ValidatePrepared` accept the `$anon:` package (rpcEntrypoint set, return type known).
- `Anonymous_kernel_capability_gating_derived_from_lambda_body` — required capabilities equal exactly the
  host-binding set; install fails when a grant is missing.
- `No_capture_invoke_uses_empty_argument_frame` — exercises the zero-argument `BuildRpcInput` path.
- `Implicit_capture_reflection_round_trips_sync_in_and_sync_out`.
- `Implicit_capture_fody_weaver_rewrites_safe_display_class_to_direct_field_access`.
- `Concurrent_first_invokes_install_once` — `EnsureAnonymousKernelAsync` does not double-install.

### Exit criteria (Phase 2)

Build green; GameServer sample `InvokeAsync` calls for no-capture and implicit-capture lambdas compile, lower,
install, run sandboxed, and return the correct values end-to-end; capability gating denies a lambda that
touches an ungranted binding.

---

## Phase 3 — `InvokeAsync` explicit capture-bag sync-in/out

**Goal.** Support sync-in/out without relying on compiler closure internals. The caller passes a mutable
capture object explicitly, and the lambda takes that object as its second parameter:

```csharp
await server.InvokeAsync(capture, async (IGameWorldAccess world, MonsterProbeCapture bag) =>
{
    var monster = world.GetMonster(bag.MonsterId);
    bag.LastHealth = monster.Health;
    return monster.Name;
});
```

### Implemented decisions

- Capture-bag calls reject ambient closure captures; lambda-only implicit captures are handled by the
  reflection-backed convenience path.
- The capture bag is encoded as one `KernelRpcValue.Record` argument.
- Reads like `bag.MonsterId` lower to `record.get(Var("bag"), index)`.
- Simple assignments to settable bag properties lower to generated sync-out locals.
- If sync-out exists, each return is structurally wrapped as `Record([returnValue, syncOut0, ...])`.
- The existing `InvokeServerExtensionAsync` response carries that single record; no new IPC method or codec is
  needed.
- The generated interceptor checks the expected response field count and writes decoded sync-out values
  back onto the same bag object after the await.

### Tests (Phase 3)

- Generation coverage for capture-bag parameter JSON and sync-out assignment emission.
- Runtime coverage for record argument encoding, response envelope decoding, and bag write-back.
- GameServer end-to-end sample coverage through the real server IPC path.

---

## Phase 4 — richer object-returning world surface

**Goal.** Support `world.GetMonster(id)` returning an object snapshot whose
`.Name/.Id/.Health/.Level` the lambda reads, instead of forcing the kernel to assemble values from separate
scalar bindings.

### Why it is the heaviest piece (verified)

`DotBoxDRpcJsonLowerer` already lowers record DTO member access to `record.get`; the implementation adds the
server-side `MonsterSnapshot` binding descriptor and uses that existing member-access path.

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
| `samples/.../Server.Abstractions/IGameWorldAccess.cs` | **Modify.** Add `MonsterSnapshot GetMonster(string entityId)` (positional record `MonsterSnapshot(string Id, string Name, int Health, int Level, int Position)`) with `[HostBinding("host.world.getMonster", "game.world.monster.read.snapshot", SandboxEffect.Cpu \| SandboxEffect.Alloc \| SandboxEffect.HostStateRead)]`. |
| `samples/.../Server/Simulation/GameWorldHost.cs` (binding registration) | **Modify.** Register the matching binding with `ReturnType = SandboxType.Record([String,String,Int,Int,Int])` backed by the live `GameWorld`/`GameEntity` lookup. `BindingRegistryValidator` already accepts Records recursively — not a blocker. |
| `src/CodeGeneration/.../Rpc/DotBoxDRpcJsonLowerer.Expressions.cs` | **Reuse.** Record DTO member access already lowers via `record.get(fieldIndex)` by declaration order. |
| `samples/.../Plugin/Client/RemotePluginServer.cs` (`RemoteMonsterControl`) | **Modify (optional).** Add an ordinary IPC `GetMonsterAsync(id)` only if non-sandbox plugin code (outside `InvokeAsync`) also needs the snapshot. Not required for the `InvokeAsync` lambda path. |
| `samples/.../Plugin/Program.cs` | **Modify.** Add `InvokeAsync` examples using `world.GetMonster(id).Health` and the capture-bag path using `world.GetMonster(id).Name`. |

The nested spelling `world.Monsters.Get(id)` remains a possible ergonomic alias, but the implemented verified
binding is flat and matches the rest of `IGameWorldAccess`.

### Tests (Phase 4)

- `Object_binding_get_monster_lowers_to_record_typed_call`.
- `Member_access_on_snapshot_lowers_to_record_get_by_declaration_order`.
- `Snapshot_field_order_mismatch_fails_registration`.
- `Method_call_on_snapshot_fails_safe` (no package emitted).
- `Snapshot_read_contributes_only_its_own_capability` (install deny without the grant).
- End-to-end: an `InvokeAsync` lambda using `world.GetMonster("monster-2").Health` returns the live value.

### Exit criteria (Phase 4)

The flat snapshot example — `world.GetMonster(id)` with `.Name/.Id/.Health/.Level` reads inside the
`InvokeAsync` lambda — compiles, lowers (Record binding + `record.get` member access), installs under the
snapshot capability, runs sandboxed, and returns the correct value. The scalar binding path still works
unchanged.

---

## Backward compatibility

**No public or internal API is removed in any phase.** The contract is additive throughout.

- **Imperative registration is untouched.** `new RemotePluginServer(control)` +
  `server.Kernels.Register<…>()` + `server.KernelRpc.Register<…>()` compile and run exactly as today
  (`Client/RemotePluginServer.cs` is unchanged in Phase 1; Phase 2 only **adds** generated facade/service
  members). The existing `Program.cs` block is preserved as the imperative demonstration.
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
  `InstallServerExtensionAsync`, are added to the session's `_owned` set, and are revoked on disconnect like
  any named kernel. No new lifecycle.
- **Wire/IPC compatibility.** All phases keep the existing IPC contract. Phase 3 carries sync-out as one
  `KernelRpcValue.Record` returned by the existing `InvokeServerExtensionAsync` method;
  `InstallServerExtensionAsync` is unchanged.

---

## Risks and limits

- **[HIGH→resolved with two paths plus IL rewrite] Interceptor closure-local capture marshalling is
  infeasible in source.** Generated interceptor methods cannot read or write caller locals by name. The
  implemented stable path is an explicit mutable capture-bag overload on `InvokeAsync`; the lambda-only
  overload also supports a reflection fallback. `DotBoxD.Plugins.Fody` opportunistically rewrites safe
  compiler-generated display-class shapes to static field access after compilation and keeps the fallback
  when it cannot prove the shape.
- **[HIGH→resolved] `InterceptsLocationAttribute` hint-name collision.** A compilation with both a hook
  chain and an `InvokeAsync` would crash the generator on a duplicate `AddSource`. Resolved by the shared
  one-shot emitter (Phase 2 prerequisite).
- **[HIGH→resolved] Concurrent anonymous-kernel install race.** Without serialization, a second same-owner
  install revokes the in-flight kernel mid-execution. Resolved by
  `ConcurrentDictionary<string, Lazy<Task<string>>>` install-once-per-id.
- **[MEDIUM] Multi-return sync-out wrapping.** Wrapping `record.new` at every return site is the correct,
  structural approach; the simpler single-return constraint (with a diagnostic) is an acceptable v1
  reduction if schedule-bound. Either is sound; **string substitution on the lowered JSON is not** and is
  excluded.
- **[MEDIUM] Sync-out response field count.** `PluginManifest.Metadata` does not exist. The implementation
  uses the generated interceptor's expected field count and validates the response record before assigning
  sync-out values. Do not invent a manifest field.
- **[MEDIUM] Nullable-reference capture-bag fields.** The IR scalar type system cannot model
  "String-or-null"; `KernelRpcValue.String(null)` coerces to empty. Capture bags should use non-nullable
  fields or accept null→empty with a documented caveat.
- **[MEDIUM] Builder testability.** `tests/DotBoxD.Kernels.Tests` cannot see sample-internal types; a new
  sample-side test project with `InternalsVisibleTo` is required (Phase 1). Do not claim the existing test
  harness is reused unchanged for builder runtime tests.
- **[LOW] Heterogeneous multi-capture sync-in.** Works because `KernelRpcValueConverter.ToSandboxValue`
  validates each wire arg against its own IR parameter type; `BuildRpcInput`'s
  `FromList(values, values[0].Type)` element-type tag is an internal positional-frame detail. Covered by a
  test.
- **[MEDIUM→resolved] Object-returning world surface.** Implemented as the flat `world.GetMonster(id)`
  snapshot binding with record-member access lowered through `record.get`.

---

## Critical files (by phase)

- **Phase 1:** `samples/.../Examples.GameServer.Plugin/Client/RemotePluginServerBuilder.cs` (new),
  `samples/.../Examples.GameServer.Plugin/Program.cs`, `samples/.../Examples.GameServer.Plugin.Tests/**`
  (new), `DotBoxD.slnx`.
- **Phase 2:** `src/CodeGeneration/.../PluginPackageGenerator.cs`,
  `src/CodeGeneration/.../HookChains/InterceptsLocationAttributeEmitter.cs` (new),
  `src/CodeGeneration/.../InvokeAsync/InvokeAsyncModelFactory.cs` (new),
  `src/CodeGeneration/.../InvokeAsync/InvokeAsyncInterceptorEmitter.cs` (new),
  `src/CodeGeneration/DotBoxD.Plugins.Fody/**` (new),
  `src/CodeGeneration/.../InvokeAsync/InvokeAsyncReceiverResolver.cs`,
  generated plugin server facade/service accessor output.
- **Phase 3:** `src/CodeGeneration/.../InvokeAsync/InvokeAsyncModelFactory.cs`,
  `…/InvokeAsyncCallShape*.cs`, `…/InvokeAsyncArgumentWriterSource.cs`,
  `…/InvokeAsyncInterceptorEmitter.cs`, and `…/Rpc/DotBoxDRpcJsonLowerer.cs`.
- **Phase 4:** `samples/.../Server.Abstractions/IGameWorldAccess.cs`,
  `samples/.../Server/Simulation/GameWorldHost.cs`,
  `samples/.../Server/Simulation/GameWorld.cs`, and `samples/.../Plugin/Program.cs`.

## Docs

Following `docs/design/plugin-fluent-hooks-api/`, new docs live under
`docs/design/remote-plugin-server-builder/`:
- `plan.md` — this document.
- `invoke-async.md` — the InvokeAsync inline-kernel deep dive (detection, lowering, explicit capture-bag
  marshalling, wire envelope, capability gating, identity).

## Suggested delivery

Land **Phase 1** first (builder). Then **Phase 2** (anonymous no-capture InvokeAsync). Then **Phase 3**
(explicit capture-bag sync-in/out). Then **Phase 4** (flat object snapshot surface via `world.GetMonster`).
Each phase is a separate commit/PR with its own green build + tests.
