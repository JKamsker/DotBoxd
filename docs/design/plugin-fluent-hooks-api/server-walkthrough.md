# Server process — what the code looks like after the plan

Companion to [plan.md](plan.md). Plugin side: [plugin-walkthrough.md](plugin-walkthrough.md).

This shows the **end-state code** for the trusted host process (`SafeIR.Game.Server`) once
Phases A–C land. It is illustrative, not a diff — focus on *how the project reads*, not exact lines.

## The mental model in one picture

```
┌─────────────────────────────┐         IPC (named pipe)        ┌──────────────────────────────┐
│  SafeIR.Game.Plugin          │  ─────────────────────────────▶ │  SafeIR.Game.Server          │
│  (untrusted author code)     │   ships verified IR + settings  │  (trusted host)              │
│                              │                                  │                              │
│  • Kernels (plain C#)        │                                  │  • Owns the SandboxPolicy    │
│  • server.Hooks.On<>()...    │                                  │  • Runs IR in the sandbox    │
│  • server.Events.On<>()...   │                                  │  • Drives the simulation     │
└─────────────────────────────┘                                  └──────────────────────────────┘
        │                                                                    ▲
        │ source generator (SafeIR.PluginAnalyzer)                          │
        ▼                                                                    │
   lowers kernels + Where/Select/InvokeKernel lambdas  ────────────────────▶ opaque verified IR
```

The server's responsibilities: it **owns the policy**, receives verified IR (never source), runs that
IR in a sandbox, drives the simulation, and exposes the control plane the plugin calls.

---

## The contract it exposes (shared)

These live in `SafeIR.Game.Server.Abstractions`, referenced by both processes. **Unchanged by the
plan** — shown here so the rest reads clearly.

### IPC control service

The plugin ships IR and tunes settings over this; the plugin's shim wraps it, so plugin example code
never calls it directly.

```csharp
[ShaRpcService]
public interface IGamePluginControlService
{
    ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);

    ValueTask UpdateSettingsAsync(
        string pluginId,
        LiveSettingUpdate[] updates,
        bool atomic = false,
        CancellationToken ct = default);

    ValueTask<WorldSnapshot> GetWorldAsync(CancellationToken ct = default);
    ValueTask<string[]> DrainEffectsAsync(CancellationToken ct = default);
}
```

### Event adapters

Each event (`MonsterAggroEvent`, `AttackEvent` — defined in
[plugin-walkthrough.md](plugin-walkthrough.md)) ships an **adapter** that tells the sandbox how to
marshal the event into IR values. This is what lets a kernel's IR read `e.Distance` etc. The server
registers these adapters at startup.

```csharp
public sealed class MonsterAggroEventAdapter : IPluginEventAdapter<MonsterAggroEvent>
{
    public static MonsterAggroEventAdapter Instance { get; } = new();

    public string EventName => "MonsterAggroEvent";

    public IReadOnlyList<Parameter> Parameters { get; } =
    [
        new("e_MonsterId",    SandboxType.String),
        new("e_PlayerId",     SandboxType.String),
        new("e_Distance",     SandboxType.I32),
        new("e_MonsterLevel", SandboxType.I32),
        new("e_PlayerLevel",  SandboxType.I32),
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(MonsterAggroEvent e) =>
    [
        SandboxValue.FromString(e.MonsterId),
        SandboxValue.FromString(e.PlayerId),
        SandboxValue.FromInt32(e.Distance),
        SandboxValue.FromInt32(e.MonsterLevel),
        SandboxValue.FromInt32(e.PlayerLevel),
    ];
}
```

---

## 1. Policy lives here — and only here

The server owns the sandbox policy; the plugin no longer carries a duplicate (the old
`PluginHostPolicy` was deleted in Phase A2).

```csharp
namespace SafeIR.Game.Server;

internal static class ServerPolicy
{
    public static SandboxPolicy Create() =>
        SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()   // the only effect kernels are allowed: emit a host message
            .WithFuel(100_000)         // deterministic execution budget
            .WithMaxHostCalls(1_000)
            .Build();
}
```

## 2. `Program` — build world, listen, launch the plugin, run

```csharp
using SafeIR.Game.Server;
using SafeIR.Transport.Ipc;

// (a) Build the plugin server with the server-owned policy, register event adapters.
var sink = new GameCommandSink();
using var server = PluginServer.Create(sink, defaultPolicy: ServerPolicy.Create());
server.RegisterEventAdapter(MonsterAggroEventAdapter.Instance);
server.RegisterEventAdapter(AttackEventAdapter.Instance);

// (b) The world publishes events through the hook pipeline.
var world = GameWorld.CreateDefault(server.Hooks);
sink.Bind(world);

// (c) Baseline phase: no plugins yet -> monsters bully the weak players.
//     ... tick the world a few times, measure damage ...

// (d) Start the IPC control plane on a high-entropy pipe name.
var pipeName = "safe-ir-game-" + Guid.NewGuid().ToString("N");
var service  = new GamePluginControlService(server, sink, world);
await using var host = SafeIrShaRpcMessagePackIpc.ListenNamedPipe(
    pipeName, peer => peer.ProvideGamePluginControlService(service));
await host.StartAsync();

// (e) Launch the plugin child process; it declares hooks + ships IR, then exits.
var process = PluginLauncher.Launch(pipeName);   // renamed from PluginHostLauncher
await process.WaitForExitAsync();

// (f) With-plugin phase: the untrusted kernels now run sandboxed and change behavior.
//     ... tick again, show the guardian calming / retaliation taunting ...

await host.StopAsync();
return 0;
```

## 3. The server-side fluent API is the *same shape* the plugin uses

The plugin's shim mirrors what the server itself exposes via `server.Hooks` / `server.Events`. On the
server, `On<TEvent>()` returns a `HookPipeline<TEvent>` that supports the full chain. The world drives
it by publishing:

```csharp
// Inside the simulation (server side), publishing an event runs the installed pipeline:
await server.Hooks.PublishAsync(
    new MonsterAggroEvent(monster.Id, target.Id, distance, monster.Level, target.Level),
    cancellationToken);
```

After Phase B, the pipeline gains `Select` (re-typing the flowing element), `InvokeLocal` /
`InvokeKernel` terminals, a `UseKernel<T>(filter)` overload, and auto-install:

```csharp
public sealed class HookPipeline<TEvent>
{
    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter);

    // NEW: project the flowing element to a different type for downstream stages.
    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection);

    // Native host code (was InvokeHostHandler; that name becomes an [Obsolete] forwarder).
    public HookPipeline<TEvent> InvokeLocal(Func<TEvent, HookContext, ValueTask> handler);

    // The API the analyzer lowers. Un-lowered, its body throws SGP040 so it never runs unsandboxed.
    public HookPipeline<TEvent> InvokeKernel(Func<TEvent, HookContext, ValueTask> handler);

    // NEW: optional gate that runs BEFORE the sandbox; the kernel's ShouldHandle still runs inside.
    public ValueTask<InstalledKernel> UseKernel<TKernel>(
        Func<TEvent, HookContext, bool>? filter = null) where TKernel : class;
}
```

`UseKernel<T>()` resolution changes from "throw if missing" to **auto-install**:

```
UseKernel<T>()
   → KernelRegistry.TryGetByKernelType<T>      (already installed? use it)
   → else KernelPackageRegistry.TryGetFactory  (analyzer-generated [ModuleInitializer] registered it)
        → InstallAsync(package)                 (ship the IR, sandbox-verify, wire it)
```

`server.Events` is the fire-and-forget mirror of `server.Hooks` (same surface; isolates handler
exceptions and does not await decisions). See the plugin walkthrough for the Hooks-vs-Events
contrast from the author's side.
