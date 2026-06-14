# Server process — what the code looks like after the plan

Companion to [plan.md](plan.md). Plugin side: [plugin-walkthrough.md](plugin-walkthrough.md).

This shows the **end-state code** for the trusted host process (`DotBoxd.Kernels.Game.Server`) once
Phases A–C land. It is illustrative, not a diff — focus on *how the project reads*, not exact lines.

## The mental model in one picture

```
┌─────────────────────────────┐         IPC (named pipe)        ┌──────────────────────────────┐
│  DotBoxd.Kernels.Game.Plugin          │  ─────────────────────────────▶ │  DotBoxd.Kernels.Game.Server          │
│  (untrusted author code)     │   ships verified IR + settings  │  (trusted host)              │
│                              │                                  │                              │
│  • Kernels (plain C#)        │                                  │  • Owns the SandboxPolicy    │
│  • server.Hooks.On<>()...    │                                  │  • Runs IR in the sandbox    │
│  • server.Events.On<>()...   │                                  │  • Drives the simulation     │
└─────────────────────────────┘                                  └──────────────────────────────┘
        │                                                                    ▲
        │ source generator (DotBoxd.Plugins.Analyzer)                          │
        ▼                                                                    │
   lowers kernels + Where/Select/InvokeKernel lambdas  ────────────────────▶ opaque verified IR
```

The server's responsibilities: it **owns the policy**, receives verified IR (never source), runs that
IR in a sandbox, drives the simulation, and exposes the control plane the plugin calls.

---

## The contract it exposes (shared)

These live in `DotBoxd.Kernels.Game.Server.Abstractions`, referenced by both processes.

### Service contracts — what the server lets plugins implement

The server publishes a domain interface per behavior it accepts. Each **extends the framework's
`IEventKernel<TEvent>`**, so it names the contract *and* (transitively) declares the event it handles.
A plugin kernel implements one of these; the server wires it generically. See
[kernel-binding-model.md](kernel-binding-model.md).

```csharp
public interface IMonsterAggroService : IEventKernel<MonsterAggroEvent> { }
public interface IAttackService        : IEventKernel<AttackEvent> { }
```

### IPC control service

The plugin ships IR and tunes settings over this; the plugin's shim wraps it, so plugin example code
never calls it directly.

```csharp
[DotBoxdService]
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

### Event adapters — inferred, not hand-written

An **adapter** tells the sandbox how to marshal an event into IR values (this is what lets a kernel's
IR read `e.Distance`). You **do not write one**: `On<TEvent>()` resolves a convention adapter that
reflects the record's readable properties in constructor order, names each sandbox parameter
`e_<PropertyName>`, and maps CLR types to sandbox types. So the events stay plain records (see
[plugin-walkthrough.md](plugin-walkthrough.md)) and the server registers nothing.

Reach for an explicit adapter or the property attributes (`[OpaqueId]`, `[SandboxParam]`,
`[SandboxIgnore]`) only when you need opaque-id branding or custom parameter names — see
[ownership-auth-and-policy.md](ownership-auth-and-policy.md) §3. The hand-written
`MonsterAggroEventAdapter`/`AttackEventAdapter` from the old example are deleted.

---

## 1. Policy lives here — as the global *ceiling*

The server owns the sandbox policy; the plugin no longer carries a duplicate (the old
`PluginHostPolicy` was deleted in Phase A2). `ServerPolicy.Create()` is the **global ceiling**: a
per-plugin policy resolver narrows it per install (never widens it) from each plugin's identity,
signed grant, and manifest request. See [ownership-auth-and-policy.md](ownership-auth-and-policy.md)
§4 for the full auth + per-plugin-limit model.

```csharp
namespace DotBoxd.Kernels.Game.Server;

internal static class ServerPolicy
{
    // The maximum any kernel may be granted; the resolver clamps each plugin to a subset of this.
    public static SandboxPolicy CreateCeiling() =>
        SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()   // the only effect kernels are allowed: emit a host message
            .WithFuel(100_000)         // deterministic execution budget
            .WithMaxHostCalls(1_000)
            .Build();
}
```

## 2. `Program` — a full class (mirrors the plugin)

`Program` is a real `internal static class` with `Main`, like the plugin. Top-level statements move
into `Main`; the `0`/`1` exit-code contract is preserved; the local helpers become `private static`
methods on `Program`.

```csharp
namespace DotBoxd.Kernels.Game.Server;

using DotBoxd.Kernels.Transport.Ipc;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // (a) Build the plugin server with the global policy ceiling. No adapter registration —
        //     On<TEvent>() infers a convention adapter from the event record.
        var sink = new GameCommandSink();
        using var server = PluginServer.Create(sink, policyCeiling: ServerPolicy.CreateCeiling());

        // (b) The world publishes events through the hook pipeline.
        var world = GameWorld.CreateDefault(server.Hooks);
        sink.Bind(world);

        // (c) Baseline phase: no plugins yet -> monsters bully the weak players.
        //     ... tick the world a few times, measure damage ...

        // (d) Start the IPC control plane on a high-entropy pipe name. Each peer gets its own
        //     authenticated session; dropping the connection revokes the kernels it owns
        //     (see ownership-auth-and-policy.md §2 + §4).
        var pipeName = "dotboxd-game-" + Guid.NewGuid().ToString("N");
        var authenticator = new LocalProcessAuthenticator();   // server spawned the child → trusted
        await using var host = DotBoxdDotBoxdRpcMessagePackIpc.ListenNamedPipe(pipeName, peer =>
        {
            var session = server.CreateSession(authenticator.Authenticate(peer));
            peer.ProvideGamePluginControlService(new GamePluginControlService(session, sink, world));
            peer.OnDisconnected(() => session.Dispose());      // revoke owned kernels on drop
        });
        await host.StartAsync();

        // (e) Launch the plugin child process; it declares hooks + ships IR, then exits.
        var process = PluginLauncher.Launch(pipeName);   // renamed from PluginHostLauncher
        await process.WaitForExitAsync();

        // (f) With-plugin phase: the untrusted kernels now run sandboxed and change behavior.
        //     ... tick again, show the guardian calming / retaliation taunting ...

        await host.StopAsync();
        return 0;
    }
}
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
`InvokeKernel` terminals, and auto-install. Pre-kernel gating is a fluent `Where` (no `filter:`
parameter) — see [ownership-auth-and-policy.md](ownership-auth-and-policy.md) §1:

```csharp
public sealed class HookPipeline<TEvent>
{
    // Gate. Before a UseKernel terminal these Where(s) are lowered and AND-composed into the
    // kernel's gate at compile time; the kernel's own ShouldHandle still runs after.
    public HookPipeline<TEvent> Where(Func<TEvent, HookContext, bool> filter);

    // NEW: project the flowing element to a different type for downstream stages.
    public HookStage<TEvent, TNext> Select<TNext>(Func<TEvent, HookContext, TNext> projection);

    // Native host code (was InvokeHostHandler; that name becomes an [Obsolete] forwarder).
    public HookPipeline<TEvent> InvokeLocal(Func<TEvent, HookContext, ValueTask> handler);

    // The API the analyzer lowers. Un-lowered, its body throws DBXK040 so it never runs unsandboxed.
    public HookPipeline<TEvent> InvokeKernel(Func<TEvent, HookContext, ValueTask> handler);

    // Internal wiring primitive only — binds an already-installed kernel. The public way to bind a
    // kernel class is server.Kernels.Register<TService, TKernel>() (see kernel-binding-model.md §1).
    internal HookPipeline<TEvent> UseKernel(InstalledKernel kernel);
}
```

Binding a kernel **class** is no longer a hook-chain terminal — it moved to
`server.Kernels.Register<TService, TKernel>()`. That call resolves the kernel package, installs it
through the owning session under the resolved per-plugin policy, and wires it **generically** —
resolving the adapter by the manifest's event name instead of the old hardcoded `WireHook` switch
([GamePluginControlService.cs:59-77](../../../examples/GameServer/DotBoxd.Kernels.Game.Server/Ipc/GamePluginControlService.cs)
is deleted). See [kernel-binding-model.md](kernel-binding-model.md) §4.

`server.Events` is the fire-and-forget mirror of `server.Hooks` (same chain surface; isolates handler
exceptions and does not await decisions). See the plugin walkthrough for the Hooks-vs-Events
contrast from the author's side.
