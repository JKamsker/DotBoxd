# Server process — what the code looks like after the plan

Companion to [plan.md](plan.md). Plugin side: [plugin-walkthrough.md](plugin-walkthrough.md).

This shows the **end-state code** for the trusted host process (`DotBoxD.Kernels.Game.Server`) once
Phases A–C land. It is illustrative, not a diff — focus on *how the project reads*, not exact lines.

## The mental model in one picture

```
┌─────────────────────────────┐         IPC (named pipe)        ┌──────────────────────────────┐
│  DotBoxD.Kernels.Game.Plugin          │  ─────────────────────────────▶ │  DotBoxD.Kernels.Game.Server          │
│  (untrusted author code)     │   ships verified IR + settings  │  (trusted host)              │
│                              │                                  │                              │
│  • Kernels (plain C#)        │                                  │  • Owns the SandboxPolicy    │
│  • server.Hooks.On<>()...    │                                  │  • Runs IR in the sandbox    │
│  • server.Events.On<>()...   │                                  │  • Drives the simulation     │
└─────────────────────────────┘                                  └──────────────────────────────┘
        │                                                                    ▲
        │ source generator (DotBoxD.Plugins.Analyzer)                          │
        ▼                                                                    │
   lowers kernels + Where/Select/InvokeKernel lambdas  ────────────────────▶ opaque verified IR
```

The server's responsibilities: it **owns the policy**, receives verified IR (never source), runs that
IR in a sandbox, drives the simulation, and exposes the control plane the plugin calls.

---

## The contract it exposes (shared)

These live in `DotBoxD.Kernels.Game.Server.Abstractions`, referenced by both processes.

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
[DotBoxDService]
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
namespace DotBoxD.Kernels.Game.Server;

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
namespace DotBoxD.Kernels.Game.Server;

using DotBoxD.Pushdown.Services;

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
        await using var host = RpcMessagePackIpc.ListenNamedPipe(pipeName, peer =>
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

The plugin's generated facade mirrors what the server itself exposes via `server.Hooks` /
`server.Subscriptions`, but it owns a plugin-side partial context type by convention. For
`GamePluginServer`, the generator emits `GamePluginContext`, `GamePluginHookRegistry`, and
`GamePluginSubscriptionRegistry`; parameterless `server.Hooks.On<TEvent>()` and
`server.Subscriptions.On<TEvent>()` use `GamePluginContext` automatically. The lower-level runtime
still exposes `On<TEvent, TServerContext>(raw => ...)` as an escape hatch for explicit contexts.

```csharp
// Inside the simulation (server side), publishing an event runs the installed pipeline:
await server.Hooks.PublishAsync(
    new MonsterAggroEvent(monster.Id, target.Id, distance, monster.Level, target.Level),
    cancellationToken);
```

The ordinary fluent authoring shape is consistent across hooks, subscriptions, and stages:
one-parameter lambdas receive only the current event/value; two-parameter lambdas receive the
current event/value first and the server context second. The same rule applies to `Where`,
`Select`, `Run`, `RunLocal`, `Register`, and `RegisterLocal` wherever those terminals exist.

```csharp
public sealed class HookPipeline<TEvent, TServerContext>
{
    public HookPipeline<TEvent, TServerContext> Where(Func<TEvent, bool> filter);
    public HookPipeline<TEvent, TServerContext> Where(Func<TEvent, TServerContext, bool> filter);

    public HookStage<TEvent, TNext, TServerContext> Select<TNext>(Func<TEvent, TNext> projection);
    public HookStage<TEvent, TNext, TServerContext> Select<TNext>(
        Func<TEvent, TServerContext, TNext> projection);

    // Lowered by the analyzer to verified IR. Un-lowered, it throws.
    public HookPipeline<TEvent, TServerContext> Run(Func<TEvent, ValueTask> handler);
    public HookPipeline<TEvent, TServerContext> Run(Func<TEvent, TServerContext, ValueTask> handler);

    // Native host/plugin process code.
    public HookPipeline<TEvent, TServerContext> RunLocal(Func<TEvent, ValueTask> handler);
    public HookPipeline<TEvent, TServerContext> RunLocal(
        Func<TEvent, TServerContext, ValueTask> handler);

    public HookPipeline<TEvent, TServerContext> Register<TResult>(Func<TEvent, TResult> handler);
    public HookPipeline<TEvent, TServerContext> Register<TResult>(
        Func<TEvent, TServerContext, TResult> handler);
    public HookPipeline<TEvent, TServerContext> RegisterLocal<TResult>(Func<TEvent, TResult> handler);
    public HookPipeline<TEvent, TServerContext> RegisterLocal<TResult>(
        Func<TEvent, TServerContext, TResult> handler);
}
```

Sandbox-lowered stages and terminals such as `Where`, `Select`, `Run`, and `Register` can only use
context members the analyzer knows how to lower into verified IR. The supported contract is explicit:
extend the generated partial context with pure helper methods marked `[KernelMethod]` so their bodies
inline into the chain, and mark host-backed context properties or methods with `[HostBinding]` so the
analyzer emits a sandbox host call and records the required capability/effects. Use `RunLocal` /
`RegisterLocal` when the handler needs arbitrary in-process services from the plugin-owned context.

For example, the generated context supplies the raw hook plumbing, while the plugin adds domain
members in a partial:

```csharp
public sealed partial class GamePluginContext
{
    [KernelMethod]
    public bool CanInspect(int distance) => distance <= 4;

    [HostBinding(
        "game.damage.adjustment",
        "game.damage.read.adjustment",
        SandboxEffect.Cpu | SandboxEffect.HostStateRead | SandboxEffect.Audit)]
    public int DamageAdjustment
        => throw new NotSupportedException("Lowering marker; implemented by a host binding.");
}

server.Hooks.On<DamageContext>()
    .Where((damage, ctx) => ctx.CanInspect(damage.Distance))
    .Register((damage, ctx) => new DamageResult
    {
        Success = true,
        Damage = damage.Amount + ctx.DamageAdjustment,
    });
```

The generated default context also preserves the `HookContext` conveniences:

```csharp
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 5)
    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));
```

A non-default context can still be selected explicitly when a chain needs a different facade:

```csharp
server.Hooks.On<MonsterAggroEvent, CombatHookContext>(ctx => new CombatHookContext(ctx, world))
    .Where((e, ctx) => ctx.CanReact(e.MonsterId))
    .RunLocal((e, ctx) => ctx.Telemetry.Count("aggro"));

server.Subscriptions.On<MonsterAggroEvent, TelemetrySubscriptionContext>(
        ctx => new TelemetrySubscriptionContext(ctx, metrics))
    .RunLocal((e, ctx) => ctx.Record(e.MonsterId));
```

Binding a kernel **class** is no longer a hook-chain terminal — it moved to
`server.Kernels.Register<TService, TKernel>()`. That call resolves the kernel package, installs it
through the owning session under the resolved per-plugin policy, and wires it **generically** —
resolving the adapter by the manifest's event name instead of the old hardcoded `WireHook` switch
([GamePluginControlService.cs](../../../samples/GameServer/Examples.GameServer.Server/Ipc/GamePluginControlService.cs)
is deleted). See [kernel-binding-model.md](kernel-binding-model.md) §4.

`server.Events` is the fire-and-forget mirror of `server.Hooks` (same chain surface; isolates handler
exceptions and does not await decisions). See the plugin walkthrough for the Hooks-vs-Events
contrast from the author's side.
