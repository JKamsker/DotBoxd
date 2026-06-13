# Addendum Implementation Examples

This branch implements the addendum as a hosting-layer plugin model:

- `SafeIR.Plugins` exposes live values, typed live contexts, kernel state, hook pipelines, plugin manifests, and safe message bindings.
- `SafeIR.PluginAnalyzer` provides local SDK diagnostics for forbidden File IO in kernels and unsupported live setting types.
- `SafeIR.PluginIpc.Server.Abstractions` owns the server-side event contracts that plugin clients implement against.
- Plugin packages carry JSON Safe IR plus manifest metadata. The server validates manifest identity, declared effects, and the Safe IR module with the existing Safe IR validator before installation.
- Hook handlers run through `SandboxHost.ExecuteAsync`. The local examples reference a generated package factory for trusted development-time convenience; production upload accepts serialized JSON package data and does not load arbitrary plugin DLLs.

## Recommended Documentation Walkthrough

The public documentation should lead with the kernel-class authoring model, then show how the server installs and runs the lowered Safe IR package. The snippets below follow the addendum's recommended order.

### 1. Implement A Simple Filter

Use simple filters for pure read-only decisions.

```csharp
public interface IItemFilter
{
    bool Accept(ItemView item, PlayerView player);
}

public sealed record ItemView(string Id, Rarity Rarity);

public sealed record PlayerView(string Id, int Level);

public enum Rarity
{
    Common,
    Rare,
    Epic,
    Legendary
}

public sealed partial class EpicItemsOnly : IItemFilter
{
    public bool Accept(ItemView item, PlayerView player)
    {
        return item.Rarity >= Rarity.Epic;
    }
}
```

Simple filters request CPU-only execution. They do not mutate server state and do not receive raw host services.
This simple contract is host-side C# guidance today; the current SafeIR source generator only
lowers `IEventKernel<TEvent>` kernels into plugin packages.

### 2. Implement A Kernel

Use kernels when the plugin needs both a server-side filter and an approved action path. The current sample kernel lives at `examples\LocalPlugin\SafeIR.PluginLocal\FireDamageKernel.cs`.

The authoring contracts (`[Plugin]`, `IEventKernel<TEvent>`, `HookContext`, `IPluginMessageSink`,
`IPluginEventAdapter<TEvent>`, `LiveSettingAttribute`) live in the purpose-agnostic
`SafeIR.Server.Abstractions` package; add `using SafeIR.Server.Abstractions;` to kernel sources.

```csharp
using SafeIR.Server.Abstractions;

[Plugin("fire-damage")]
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    public bool ShouldHandle(DamageEvent e, HookContext ctx)
    {
        return e.DamageType == "fire" && e.Amount >= 100;
    }

    public void Handle(DamageEvent e, HookContext ctx)
    {
        ctx.Messages.Send(e.TargetId, "Ouch, fire.");
    }
}
```

`ShouldHandle` is the server-side filter. `Handle` can only perform actions exposed by the approved context facade.

### 3. Add Live Settings

Kernel properties become live settings when the generated package manifest exposes them as live state metadata. The authoring-side C# uses `[LiveSetting]`; the source generator mirrors the same settings into the manifest.

```csharp
[Plugin("fire-damage")]
public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
{
    [LiveSetting]
    public string DamageType { get; set; } = "fire";

    [LiveSetting]
    [Range(0, 10_000)]
    public int MinDamage { get; set; } = 100;

    public bool ShouldHandle(DamageEvent e, HookContext ctx)
    {
        return e.DamageType == DamageType &&
               e.Amount >= MinDamage;
    }

    public void Handle(DamageEvent e, HookContext ctx)
    {
        ctx.Messages.Send(e.TargetId, "Ouch, fire.");
    }
}
```

The generated sample manifest includes the matching metadata:

```csharp
new LiveSettingDefinition("DamageType", "string", "fire");
new LiveSettingDefinition("MinDamage", "int", 100, 0, 10_000);
```

The runtime enforces supported live setting types and numeric ranges during install and live updates.

### 4. Register A Hook

The server registers hooks against event adapters. Event adapters convert trusted host event snapshots into `SandboxValue` inputs for the verified IR entrypoints.

```csharp
var messages = new InMemoryPluginMessageSink();
var server = PluginServer.Create(messages, defaultPolicy: PluginMessagePolicy());
server.RegisterEventAdapter(DamageEventAdapter.Instance);
await server.InstallAsync(FireDamagePluginPackage.Create());

server.Hooks.On<DamageEvent>()
    .UseKernel<FireDamageKernel>();
```

Production servers should register reviewed event adapters before installing packages or wiring
hooks. Convention/discovery adapters are a development convenience; the production posture is an
explicit server-owned whitelist of event shapes and fields.

The pipeline flow is:

```text
DamageEvent
  -> server-side hook filters
  -> kernel.ShouldHandle
  -> kernel.Handle
```

### 5. Update Settings At Runtime

Live setting changes are applied to future hook executions without reinstalling the plugin or rebuilding the hook pipeline. Use `ModifyAsync` when multiple settings should become visible as one validated batch.

```csharp
var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

await kernel.ModifyAsync(state => {
    state.MinDamage = 250;
    state.DamageType = "ice";
});

await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-2"));
```

Pass `atomic: true` when the update must also wait for any in-flight kernel execution and prevent a new execution from crossing the commit boundary:

```csharp
await kernel.ModifyAsync(
    state => {
        state.MinDamage = 250;
        state.DamageType = "ice";
    },
    atomic: true);
```

Direct `kernel.Value` assignments use synchronous synchronization by default. If a caller wants fire-and-forget direct assignments, set `UpdateMode = LiveUpdateMode.AsyncSet`. In `AsyncSet`, the typed object changes immediately, but the committed sandbox input is not synchronized until the update queue is drained. A hook execution that observes the assignment before a flush may enqueue the update and still run with the previously committed settings.

```csharp
kernel.UpdateMode = LiveUpdateMode.AsyncSet;
kernel.Value.MinDamage = 250;

// Await this before publishing when the next hook run must see the new value.
await kernel.FlushUpdatesAsync();
```

Use `ModifyAsync` instead of `AsyncSet` when multiple settings must commit together or when the caller needs validation and acknowledgement before continuing. `ModifyAsync` ignores `UpdateMode`, validates the batch, and commits it before returning.

For small scripts, the same live-update behavior is available through value bindings:

```csharp
var minDamage = server.BindValue("minDamage", 100);

server.Hooks.On<DamageEvent>()
    .Where((e, _) => e.Amount >= minDamage.Value)
    .InvokeHostHandler((e, ctx) => ctx.Messages.Send(e.TargetId, "matched"));

minDamage.Value = 250;
```

For grouped settings, use a typed live context:

```csharp
var settings = server.BindContext<IFireDamageSettings>(
    "operatorDefaults",
    value => {
        value.DamageType = "fire";
        value.MinDamage = 100;
    });

settings.Value.MinDamage = 250;
```

Over IPC, send the same update as one request:

```csharp
await service.ModifySettingsAsync(
    [
        new LiveSettingUpdate("MinDamage", "250"),
        new LiveSettingUpdate("DamageType", "ice")
    ],
    atomic: true);
```

### 6. Inspect Plugin Permissions

The plugin package manifest exposes the permissions and subscriptions that an admin UI or server owner can inspect before enabling the plugin.

```csharp
var package = FireDamagePluginPackage.Create();

foreach (var effect in package.Manifest.Effects) {
    Console.WriteLine(effect);
}

foreach (var setting in package.Manifest.LiveSettings) {
    Console.WriteLine($"{setting.Name}: {setting.Type} = {setting.DefaultValue}");
}
```

The sample package requests:

```text
Effects:
  Cpu
  Alloc
  HostStateWrite
  Audit

Capability request:
  host.message.write

Subscription:
  DamageEvent -> FireDamageKernel
```

This is the data a server owner needs to show settings, defaults, ranges, requested effects, and hook subscriptions before install.

### 7. Upload Or Install Package

The production server installs a plugin package from serialized JSON package data. The JSON envelope contains a manifest, entrypoint names if needed, and the Safe IR module. It does not contain an assembly path or plugin DLL reference.

Reference the `SafeIR.Plugins` package for `PluginPackageJsonSerializer` and the
`InstallJsonAsync` extension. The helper types are plugin-facing APIs used from the
`SafeIR.Plugins` namespace (the package references `SafeIR.Serialization.Json` for the module-IR
round trip):

```csharp
using SafeIR.Plugins;
```

```csharp
var package = FireDamagePluginPackage.Create();
var uploadJson = PluginPackageJsonSerializer.Export(package);
```

```csharp
var kernel = await server.InstallJsonAsync(uploadJson);
```

The local generated factory is still useful for SDK examples and tests, but direct in-process install is not the production upload boundary:

```csharp
var package = FireDamagePluginPackage.Create();
var kernel = await server.InstallAsync(package);
```

The default plugin server policy does not grant message-write capability. Message-sending plugins
must install with an explicit policy grant:

```csharp
static SandboxPolicy PluginMessagePolicy()
    => SandboxPolicyBuilder.Create()
        .GrantLogging()
        .GrantHostMessageWrite()
        .WithFuel(100_000)
        .WithMaxHostCalls(1_000)
        .Build();
```

If the policy does not grant `host.message.write`, package preparation fails closed with a policy diagnostic. The server still re-validates uploaded JSON packages; local analyzer diagnostics are developer-experience feedback, not the trust boundary.

### 8. Observe Runtime Execution

Installed kernels expose the most recent execution and a snapshot of per-entrypoint observations. These are host/admin status values, not plugin-controlled permissions:

```csharp
await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

var last = kernel.LastExecution;
foreach (var observation in kernel.ExecutionObservations) {
    Console.WriteLine(
        $"{observation.Entrypoint}: requested={observation.RequestedMode}, actual={observation.ActualMode}");
}
```

Each observation includes the entrypoint name, requested mode, actual mode, success flag, safe fallback reason when present, cache/materialization status, and compiled runtime envelope fields when compiled execution was used.

## Custom Host Binding Example

Host-owned bindings are the main extensibility point for exposing product-specific data and
services to verified Safe IR. The runnable example lives in
`examples\Capabilities\SafeIR.Example.Capabilities\Examples\CustomBindingExample.cs` and is exercised by
the capabilities example run and the docs smoke script.

The example authors a `tenant.lookup` binding and shows every field a binding author must decide:

```csharp
new BindingDescriptor(
    "tenant.lookup",
    SemVersion.One,
    [SandboxType.I32],
    SandboxType.I32,
    // Read-only external access still emits audit, so include the Audit effect.
    SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit,
    "tenant.read",                       // required capability (custom, so a grant validator is mandatory)
    BindingCostModel.Fixed(8),           // deterministic cost: one host call, flat fuel
    AuditLevel.PerCall,                  // external bindings must be audited
    BindingSafety.ReadOnlyExternal,      // safety classification
    InvokeTenantLookup,                  // BindingInvoker
    CompiledBinding.RuntimeStub(         // compiled-mode dispatch stub
        typeof(CompiledRuntime).FullName!,
        nameof(CompiledRuntime.CallBinding)),
    ValidateTenantReadGrant);            // CapabilityGrantValidator
```

Register it, grant the capability, import JSON IR that calls it, and inspect the value and audit:

```csharp
using var host = SandboxHost.Create(builder =>
{
    builder.AddBinding(TenantLookupBinding());
    builder.UseInterpreter();
    builder.UseCompilerIfAvailable();
});

var module = await host.ImportJsonAsync(tenantLookupJsonIr);
var policy = SandboxPolicyBuilder.Create()
    .Grant("tenant.read", new { maxTenantId = 100 },
        SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)
    .WithFuel(10_000)
    .WithMaxHostCalls(16)
    .Build();

var plan = await host.PrepareAsync(module, policy);
var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

var value = ((I32Value)result.Value!).Value;
var audit = result.AuditEvents.Single(e => e.BindingId == "tenant.lookup");
```

Safe defaults a binding author should follow:

- **Required capability**: any binding that reaches outside pure CPU must declare a capability;
  a custom (non-built-in) capability also requires a `CapabilityGrantValidator` that fails closed
  on unsupported or invalid grant parameters.
- **Deterministic cost model**: prefer `BindingCostModel.Fixed(...)` or `PerByte(...)` so fuel and
  host-call accounting are predictable.
- **Audit level**: external bindings must use at least `AuditLevel.PerCall` and emit a `BindingCall`
  audit event populated with `context.BindingAuditFields(...)`.
- **Safety classification**: pick the narrowest `BindingSafety` that fits; `ReadOnlyExternal` for
  reads, `SideEffectingExternal` for writes.
- **Resource charging**: the host charges the declared cost and one host call per invocation, visible
  through `result.ResourceUsage.HostCalls`.
- **Compiled runtime stub**: custom bindings dispatch through
  `CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding))`.

## Audit Observer Example

`SandboxHostBuilder.ForwardAuditEventsTo(...)` is the public host integration point for operational
audit streaming (telemetry, billing, incident review, compliance export). The runnable example lives
in `examples\Capabilities\SafeIR.Example.Capabilities\Examples\AuditObserverExample.cs` and is exercised by
the capabilities example run and the docs smoke script.

The example registers two observers and runs a minimal module:

```csharp
var observed = new List<SandboxAuditEvent>();

using var host = SandboxHost.Create(builder =>
{
    builder.AddDefaultPureBindings();
    builder.UseInterpreter();
    // A failing telemetry sink must not change sandbox results or starve later observers.
    builder.ForwardAuditEventsTo(_ => throw new InvalidOperationException("telemetry sink offline"));
    builder.ForwardAuditEventsTo(observed.Add);
});

var plan = await host.PrepareAsync(module, policy);
var result = await host.ExecuteAsync(plan, "main", input,
    new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

// Observed events equal the returned result's audit events, in sequence order, even though the
// first observer threw on every event.
var matchesResult = observed.SequenceEqual(result.AuditEvents);
```

The example prints that the throwing observer did not change `result.Succeeded`, that the surviving
observer received exactly `result.AuditEvents`, and that the events arrive in `SequenceNumber` order.
This is the contract documented in `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md`:
observer failures are isolated and do not change the returned `SandboxExecutionResult` or prevent
later observers from receiving the same sequenced audit events.

## Resource Limits Example

`WithFuel(...)` is only one of the public quota knobs. `SandboxPolicyBuilder` also exposes
`WithMaxLoopIterations`, `WithMaxHostCalls`, `WithWallTime`, `WithMaxCallDepth`, `WithMaxAllocatedBytes`,
the collection-shape limits (`WithMaxListLength`, `WithMaxMapEntries`, `WithMaxCollectionDepth`,
`WithMaxTotalCollectionElements`), the log limits (`WithMaxLogEvents`, `WithMaxLogMessageLength`), and the
string limits (`WithMaxStringLength`, `WithMaxTotalStringBytes`). The runnable proof lives in
`examples\Capabilities\SafeIR.Example.Capabilities\Examples\ResourceLimitsExample.cs` and is
exercised by the capabilities example run and the docs smoke script.

The example runs small JSON IR modules under intentionally tight non-fuel limits and prints the public
result code plus the matching `SandboxResourceUsage` counter for each case:

```csharp
var policy = SandboxPolicyBuilder.Create()
    .WithFuel(10_000)
    .WithMaxLoopIterations(3)
    .Build();

var plan = await host.PrepareAsync(module, policy);
var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

// result.Error?.Code is SandboxErrorCode.QuotaExceeded and
// result.ResourceUsage.LoopIterations reports the metered iterations.
```

The walkthrough covers loop-iteration exhaustion, host-call exhaustion, wall-time timeout
(`SandboxErrorCode.Timeout`, not `QuotaExceeded`), list-shape rejection, and string-shape rejection.
Each case asserts the documented `SandboxErrorCode` and the corresponding `SandboxResourceUsage` field so
integrators can recognize a denied run and read back what the runtime metered, matching the resource-usage
contract in `docs/Specs/Initial/safe-ir-sandbox-spec/spec/16-public-api.md`.

## Flagship Fire Damage Example

The flagship example is implemented in:

- `examples\LocalPlugin\SafeIR.PluginLocal\FireDamageKernel.cs`
- `examples\PluginIpc\SafeIR.PluginIpc.Server.Abstractions\DamageEvent.cs`
- `SafeIR.PluginAnalyzer` generated `FireDamagePluginPackage.g.cs`
- `examples\LocalPlugin\SafeIR.PluginLocal\Program.cs`

Mental model:

```text
Kernel properties are live settings.
ShouldHandle filters events.
Handle performs approved actions.
The server executes verified Safe IR, not arbitrary plugin DLLs.
```

## Local Kernel Example

Run the complete addendum example set, split across three topic projects:

```powershell
dotnet run --project examples\Capabilities\SafeIR.Example.Capabilities\SafeIR.Example.Capabilities.csproj
dotnet run --project examples\Hosting\SafeIR.Example.Hosting\SafeIR.Example.Hosting.csproj
dotnet run --project examples\PluginAuthoring\SafeIR.Example.PluginAuthoring\SafeIR.Example.PluginAuthoring.csproj
```

Run:

```powershell
dotnet run --project examples\LocalPlugin\SafeIR.PluginLocal\SafeIR.PluginLocal.csproj
```

The example installs the `fire-damage` kernel, publishes events, updates `MinDamage` and `DamageType` at runtime, and shows that future hook executions observe the latest live settings.

It also demonstrates:

- Level 1: host-authored `BindValue<T>`
- Level 2: host-authored `BindContext<TSettings>`
- Level 3: package/generator-backed IR kernel classes with manifest live settings

## Named-Pipe IPC Example

The IPC sample uses `SafeIR.Transport.Ipc.ShaRpc`, which wraps ShaRPC named pipes with MessagePack serialization. The pipe name is a trusted local control-plane endpoint: pass an explicit high-entropy or otherwise deployment-scoped name, and do not expose it across tenant boundaries. The shared contract project still references `ShaRPC` and `MessagePack` because service attributes and payload attributes live with the contract types.

Run the server in one terminal:

```powershell
dotnet run --project examples\PluginIpc\SafeIR.PluginIpc.Server\SafeIR.PluginIpc.Server.csproj -- safe-ir-plugin-ipc-local-demo
```

Run the client in another:

```powershell
dotnet run --project examples\PluginIpc\SafeIR.PluginIpc.Client\SafeIR.PluginIpc.Client.csproj -- safe-ir-plugin-ipc-local-demo
```

The client reads settings, publishes a matching event, changes live settings over IPC, and publishes again to prove the server-side hook pipeline uses the updated state.

## Game Server Golden Example

The golden example combines every layer of the plugin model into one runnable scenario. It lives in:

- `examples\GameServer\SafeIR.Game.Server.Abstractions` — the shared contract: the
  `MonsterAggroEvent` and `AttackEvent` records with their `IPluginEventAdapter<T>` adapters, the
  `[ShaRpcService] IGamePluginControlService` IPC contract with MessagePack DTOs, and the
  plugin -> server command DSL helpers in `GameCommands`.
- `examples\GameServer\SafeIR.Game.PluginHost` — the child process that authors two kernels
  (`GuardianKernel`, `RetaliationKernel`), previews them locally, and ships them over IPC.
- `examples\GameServer\SafeIR.Game.Server` — the parent process: a deterministic 1D simulation, the
  example-defined command sink, the IPC service, and the orchestration entrypoint.

### Filter + projection + invoke

Each kernel's `ShouldHandle` is the server-side filter and `Handle` is the approved action. The
generator lowers arithmetic and comparisons in `ShouldHandle` and a single string-concat
`ctx.Messages.Send(...)` in `Handle`:

```csharp
[Plugin("guardian")]
public sealed partial class GuardianKernel : IEventKernel<MonsterAggroEvent>
{
    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx)
        => e.MonsterLevel - e.PlayerLevel >= LevelGap &&
           e.Distance <= AggroRange &&
           e.PlayerLevel <= ProtectMaxLevel;

    public void Handle(MonsterAggroEvent e, HookContext ctx)
        => ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId + ":" + CalmStrength);
}
```

The plugin host runs the same filter/projection/invoke pipeline in-process first (a local preview)
so the author sees which events match and which command payloads the kernels emit before shipping
anything. The server then runs the identical lowered IR — never the kernel source.

### Settings binding, IPC, and the example-defined capability

The host ships each kernel as opaque verified IR with `PluginPackageJsonSerializer.Export(...)` plus
`InstallPluginAsync(json)`, then tunes live settings over IPC with one atomic
`UpdateSettingsAsync("guardian", [...], atomic: true)` batch. The server installs the IR with
`server.InstallJsonAsync(...)` and wires the hook for whichever event the kernel's manifest
subscription declares.

The plugin's only sandbox capability is `host.message.write`. The *meaning* of those messages is
defined by the example, not by SafeIR core: `Simulation\GameCommandSink.cs` implements
`IPluginMessageSink`, parses the `calm:`/`taunt:` DSL, validates it (known verb, known/opaque entity
ids, clamped strength), and applies it to the world. Invalid or unknown commands are ignored safely
and never throw back into the sandbox. The server contrasts a baseline phase (no plugins) with a
with-plugin phase to show the untrusted kernels measurably changing game behavior.

```powershell
dotnet run --project examples\GameServer\SafeIR.Game.Server\SafeIR.Game.Server.csproj
```
