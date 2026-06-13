# Plugin process — what the code looks like after the plan

Companion to [plan.md](plan.md). Server side: [server-walkthrough.md](server-walkthrough.md).

This shows the **end-state code** for the untrusted plugin process (`SafeIR.Game.Plugin`) once
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

Two key ideas this process is built around:

1. **The plugin never trusts itself.** Kernels and sandboxed lambdas are *lowered to verified IR*
   by the analyzer at build time. The server receives IR, not source.
2. **The plugin talks to the server with the same fluent API the server exposes locally.** The
   plugin holds a `RemotePluginServer` shim that *looks* like a `PluginServer` but forwards over IPC.

---

## The events it authors against (shared)

These records live in `SafeIR.Game.Server.Abstractions`, referenced by both processes. The plugin
writes kernels against them; the server registers the matching adapters (see the server walkthrough).

```csharp
namespace SafeIR.Game.Server.Abstractions;

public sealed record MonsterAggroEvent(
    string MonsterId,
    string PlayerId,
    int Distance,
    int MonsterLevel,
    int PlayerLevel);

public sealed record AttackEvent(
    string AttackerId,
    string TargetId,
    int Damage,
    int AttackerLevel);
```

---

## 1. Kernels — plain C#, lowered to verified IR

A kernel is authored as ordinary C#. The `[Plugin]` attribute + `partial` let the analyzer generate
the `{X}PluginPackage` (the verified IR + a self-registration hook). **The author writes no IR.**

```csharp
namespace SafeIR.Game.Plugin;

using System.ComponentModel.DataAnnotations;

[Plugin("guardian")]
public sealed partial class GuardianKernel : IEventKernel<MonsterAggroEvent>
{
    [LiveSetting] [Range(0, 100)] public int LevelGap        { get; set; } = 3;
    [LiveSetting] [Range(0, 100)] public int AggroRange      { get; set; } = 5;
    [LiveSetting] [Range(0, 100)] public int ProtectMaxLevel { get; set; } = 5;
    [LiveSetting]                 public string CalmStrength  { get; set; } = "20";

    // Gate: only calm when a strong monster is bullying a weak, nearby player.
    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx) =>
        e.MonsterLevel - e.PlayerLevel >= LevelGap &&
        e.Distance <= AggroRange &&
        e.PlayerLevel <= ProtectMaxLevel;

    // Effect: emit one host message (the only capability the server granted).
    public void Handle(MonsterAggroEvent e, HookContext ctx) =>
        ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId + ":" + CalmStrength);
}
```

```csharp
namespace SafeIR.Game.Plugin;

using System.ComponentModel.DataAnnotations;

[Plugin("retaliation")]
public sealed partial class RetaliationKernel : IEventKernel<AttackEvent>
{
    [LiveSetting] [Range(0, 10_000)] public int MinDamage        { get; set; } = 5;
    [LiveSetting] [Range(0, 100)]    public int MinAttackerLevel { get; set; } = 5;

    public bool ShouldHandle(AttackEvent e, HookContext ctx) =>
        e.Damage >= MinDamage && e.AttackerLevel >= MinAttackerLevel;

    public void Handle(AttackEvent e, HookContext ctx) =>
        ctx.Messages.Send(e.AttackerId, "taunt:" + e.TargetId);
}
```

## 2. `Program` — declares hooks with the server's fluent API

This is the headline change. **Before**, the plugin manually exported each kernel to JSON and called
`InstallPluginAsync` with "opaque IR" narration. **After**, it just *declares* hooks — the framework
ships and installs the kernel IR automatically.

```csharp
namespace SafeIR.Game.Plugin;

using SafeIR.Game.Plugin.Client;
using SafeIR.Transport.Ipc;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: SafeIR.Game.Plugin <named-pipe-name>");
            return 1;
        }

        // (1) Connect to the server's control plane and wrap it in a server-shaped shim.
        await using var connection =
            await SafeIrShaRpcMessagePackIpc.ConnectNamedPipeAsync(args[0]);
        var server = new RemotePluginServer(connection.Get<IGamePluginControlService>());

        // (2) Declare hooks. UseKernel<T>() ships + installs the kernel's verified IR for us —
        //     no Export, no InstallPluginAsync, no "opaque IR" narration.
        await server.Hooks.On<MonsterAggroEvent>().UseKernel<GuardianKernel>();
        await server.Hooks.On<AttackEvent>().UseKernel<RetaliationKernel>();

        // (3) Tune live settings fluently (one atomic IPC batch under the hood).
        await server.Kernels.Get("guardian")
            .Set("CalmStrength", 35)
            .Set("AggroRange", 6)
            .ApplyAsync(atomic: true);

        Console.WriteLine("[plugin] hooks declared, settings tuned. Exiting.");
        return 0;
    }
}
```

## 3. The richer pipeline (Phase B + C)

`UseKernel<T>()` is the simple terminal. The fuller API lets the author build a pipeline inline.
**Every `Where`/`Select`/`InvokeKernel` lambda is lowered to sandboxed IR**; only `InvokeLocal`
stays native host code.

```csharp
// Sandboxed: filter -> project -> filter -> sandboxed effect (all lowered to verified IR).
await server.Hooks.On<MonsterAggroEvent>()
    .Where((e, ctx)  => e.Distance <= 5)                       // sandboxed
    .Select((e, ctx) => e.MonsterLevel - e.PlayerLevel)        // sandboxed projection
    .Where((gap, ctx) => gap >= 3)                             // sandboxed, sees the projection
    .InvokeKernel((gap, ctx) => ctx.Messages.Send("monster", "calm:" + gap));

// UseKernel with an extra gate: the filter runs BEFORE the sandbox,
// the kernel's own ShouldHandle still runs inside it.
await server.Hooks.On<AttackEvent>()
    .UseKernel<RetaliationKernel>(filter: (e, ctx) => e.Damage >= 10);

// InvokeLocal = trusted native host code, NOT sandboxed (use sparingly).
server.Hooks.On<AttackEvent>()
    .InvokeLocal((e, ctx) => { Console.WriteLine($"observed {e.AttackerId}"); return ValueTask.CompletedTask; });
```

`server.Events` is the fire-and-forget mirror of `server.Hooks`. Same `Where/Select/InvokeKernel/
UseKernel` surface — the difference is intent and dispatch:

| | `server.Hooks` | `server.Events` |
|---|---|---|
| Meaning | plugin **decides** what happens | plugin is **notified** |
| Dispatch | awaited sequentially (decisions matter) | fire-and-forget, exceptions isolated |

```csharp
// Hooks: the plugin's decision feeds back into the simulation.
await server.Hooks.On<MonsterAggroEvent>().UseKernel<GuardianKernel>();

// Events: the plugin just wants to know it happened.
server.Events.On<AttackEvent>()
    .InvokeLocal((e, ctx) => { Telemetry.Count("attack"); return ValueTask.CompletedTask; });
```

## 4. The shim — `RemotePluginServer` (Phase A)

A small example-local class that gives the plugin a `PluginServer`-shaped surface while forwarding
over the unchanged IPC contract (`IGamePluginControlService`, defined in
[server-walkthrough.md](server-walkthrough.md)). `UseKernel<T>()` is `async` because it's a real
round-trip: resolve the generated package → `InstallPluginAsync`.

```csharp
namespace SafeIR.Game.Plugin.Client;

using SafeIR.Game.Server.Abstractions;

/// <summary>
/// Server-shaped facade over the IPC control service. Lets plugin code read
/// `server.Hooks.On<>().UseKernel<>()` and `server.Kernels.Get(..).Set(..).ApplyAsync()`
/// without ever touching IGamePluginControlService directly.
/// </summary>
internal sealed class RemotePluginServer
{
    private readonly IGamePluginControlService _control;

    public RemotePluginServer(IGamePluginControlService control)
    {
        _control = control;
        Hooks   = new RemoteHookRegistry(control);
        Kernels = new RemoteKernelControl(control);
    }

    public RemoteHookRegistry   Hooks   { get; }
    public RemoteKernelControl  Kernels { get; }
}

internal sealed class RemoteHookRegistry
{
    private readonly IGamePluginControlService _control;
    public RemoteHookRegistry(IGamePluginControlService control) => _control = control;

    public RemoteHookPipeline<TEvent> On<TEvent>() => new(_control);
}

internal sealed class RemoteHookPipeline<TEvent>
{
    private readonly IGamePluginControlService _control;
    public RemoteHookPipeline(IGamePluginControlService control) => _control = control;

    // Phase A: resolve the generated package and ship it. Phase B deletes the catalog and
    // resolves via the generated KernelPackageRegistry instead.
    public async Task<string> UseKernel<TKernel>() where TKernel : class
    {
        var package = KernelPackageRegistry.GetByKernelType<TKernel>(); // generated [ModuleInitializer] self-registers
        var json    = PluginPackageJsonSerializer.Export(package);
        return await _control.InstallPluginAsync(json);
    }
}

internal sealed class RemoteKernelControl
{
    private readonly IGamePluginControlService _control;
    public RemoteKernelControl(IGamePluginControlService control) => _control = control;

    public RemoteKernelHandle Get(string pluginId) => new(_control, pluginId);
}

internal sealed class RemoteKernelHandle
{
    private readonly IGamePluginControlService _control;
    private readonly string _pluginId;
    private readonly List<LiveSettingUpdate> _updates = [];

    public RemoteKernelHandle(IGamePluginControlService control, string pluginId)
    {
        _control = control;
        _pluginId = pluginId;
    }

    public RemoteKernelHandle Set(string name, object value)
    {
        _updates.Add(new LiveSettingUpdate(name, Convert.ToString(value)!));
        return this;
    }

    public ValueTask ApplyAsync(bool atomic = false) =>
        _control.UpdateSettingsAsync(_pluginId, [.. _updates], atomic);
}
```

> **What got deleted** (Phase A2): `Local/LocalPreview.cs`, `Local/PluginHostPolicy.cs`,
> `Local/RecordingMessageSink.cs`. Policy is the server's job, and the in-process preview is gone.

## 5. What the analyzer does (Phase C, behind the scenes)

The author never sees this — but it's *why* the code above is safe. For each kernel and each
`Where/Select/InvokeKernel` chain, the source generator emits a verified-IR package:

```csharp
// GENERATED — illustrative. The author wrote GuardianKernel in plain C#.
internal static class GuardianPluginPackage
{
    public static PluginPackage Create() => /* verified SafeIR module: ShouldHandle + Handle */;

    [ModuleInitializer]
    internal static void Register() =>
        KernelPackageRegistry.Register(typeof(GuardianKernel), Create);  // enables auto-install (B4)
}
```

- `Where` lambdas → AND-composed into the module's `ShouldHandle`.
- `Select` → compile-time substitution into downstream lambdas (no new runtime protocol).
- `InvokeKernel` terminal → the module's `Handle` (must be a single `ctx.Messages.Send`).
- `InvokeLocal` → left as native host code, lowered to nothing.
- Anything outside the lowerable subset → a diagnostic (`SGP110`–`SGP114`), so unsafe code fails the
  build instead of silently running native.

## 6. Side-by-side: the request in one diff

**Before (today):**

```csharp
// plugin Program.cs — manual, narrates "opaque IR"
var guardianJson = PluginPackageJsonSerializer.Export(GuardianPluginPackage.Create());
var guardianId   = await service.InstallPluginAsync(guardianJson);
await service.UpdateSettingsAsync("guardian",
    [new LiveSettingUpdate("CalmStrength", "35"), new LiveSettingUpdate("AggroRange", "6")],
    atomic: true);
```

**After (the plan):**

```csharp
// plugin Program.cs — declarative, framework ships the IR
await server.Hooks.On<MonsterAggroEvent>().UseKernel<GuardianKernel>();
await server.Kernels.Get("guardian")
    .Set("CalmStrength", 35)
    .Set("AggroRange", 6)
    .ApplyAsync(atomic: true);
```

Same two processes, same verified-IR safety guarantee, same IPC contract underneath — but the plugin
now *declares intent* with the server's fluent API, and the framework handles shipping and installing
the verified kernel IR.
