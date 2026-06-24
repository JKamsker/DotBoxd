# Plugin process — what the code looks like after the plan

Companion to [plan.md](plan.md). Server side: [server-walkthrough.md](server-walkthrough.md).

This shows the **end-state code** for the untrusted plugin process (`DotBoxD.Kernels.Game.Plugin`) once
Phases A–C land. It is illustrative, not a diff — focus on *how the project reads*, not exact lines.

## The mental model in one picture

```
┌─────────────────────────────┐         IPC (named pipe)        ┌──────────────────────────────┐
│  DotBoxD.Kernels.Game.Plugin          │  ─────────────────────────────▶ │  DotBoxD.Kernels.Game.Server          │
│  (untrusted author code)     │   ships verified IR + settings  │  (trusted host)              │
│                              │                                  │                              │
│  • Kernels (plain C#)        │                                  │  • Owns the SandboxPolicy    │
│  • server.Hooks.On<>()...    │                                  │  • Runs IR in the sandbox    │
│  • server.Subscriptions.On<>()... │                              │  • Drives the simulation     │
└─────────────────────────────┘                                  └──────────────────────────────┘
        │                                                                    ▲
        │ source generator (DotBoxD.Plugins.Analyzer)                          │
        ▼                                                                    │
   lowers kernels + Where/Select/Run lambdas  ─────────────────────────────▶ opaque verified IR
```

Two key ideas this process is built around:

1. **The plugin never trusts itself.** Kernels and sandboxed lambdas are *lowered to verified IR*
   by the analyzer at build time. The server receives IR, not source.
2. **The plugin talks to the server with the same fluent API the server exposes locally.** The
   plugin holds a `RemotePluginServer` shim that *looks* like a `PluginServer` but forwards over IPC.

---

## The events it authors against (shared)

These records live in `DotBoxD.Kernels.Game.Server.Abstractions`, referenced by both processes. The plugin
writes kernels against them. **No event adapter is needed** — the framework infers the sandbox shape
from the record's properties via a convention adapter (`e_<PropertyName>`, CLR→sandbox type mapping).
Optional property attributes (`[OpaqueId]`, `[SandboxParam]`, `[SandboxIgnore]`) cover the cases
convention can't see. See ownership-auth-and-policy.md §3.

```csharp
namespace DotBoxD.Kernels.Game.Server.Abstractions;

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

## 1. Kernels — plain C# implementing a server contract, lowered to verified IR

A kernel is authored as ordinary C# and implements one of the **server-published service contracts**
(`IMonsterAggroService : IEventKernel<MonsterAggroEvent>`, defined in the shared abstractions — see
[kernel-binding-model.md](kernel-binding-model.md) §2). The `[Plugin]` attribute + `partial` let the
analyzer generate the `{X}PluginPackage` (the verified IR + a self-registration hook). **The author
writes no IR**, and the analyzer detects the event transitively through the contract, so the IR
contract is unchanged.

```csharp
namespace DotBoxD.Kernels.Game.Plugin;

using System.ComponentModel.DataAnnotations;

[Plugin("guardian")]
public sealed partial class GuardianKernel : IMonsterAggroService   // : IEventKernel<MonsterAggroEvent>
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
namespace DotBoxD.Kernels.Game.Plugin;

using System.ComponentModel.DataAnnotations;

[Plugin("retaliation")]
public sealed partial class RetaliationKernel : IAttackService   // : IEventKernel<AttackEvent>
{
    [LiveSetting] [Range(0, 10_000)] public int MinDamage        { get; set; } = 5;
    [LiveSetting] [Range(0, 100)]    public int MinAttackerLevel { get; set; } = 5;

    public bool ShouldHandle(AttackEvent e, HookContext ctx) =>
        e.Damage >= MinDamage && e.AttackerLevel >= MinAttackerLevel;

    public void Handle(AttackEvent e, HookContext ctx) =>
        ctx.Messages.Send(e.AttackerId, "taunt:" + e.TargetId);
}
```

## 2. `Program` — registers kernels against the server's service contracts

This is the headline change. **Before**, the plugin manually exported each kernel to JSON and called
`InstallPluginAsync` with "opaque IR" narration. **After**, it *registers* each kernel as the
implementation of a server service contract — the framework ships and installs the kernel IR
automatically. See [kernel-binding-model.md](kernel-binding-model.md).

```csharp
namespace DotBoxD.Kernels.Game.Plugin;

using DotBoxD.Kernels.Game.Plugin.Client;
using DotBoxD.Pushdown.Services;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: DotBoxD.Kernels.Game.Plugin <named-pipe-name>");
            return 1;
        }

        // (1) Record each kernel against the generated hook/subscription registries. StartAsync
        //     ships + installs the verified IR for us — no Export, no InstallPluginAsync.
        using var server = GamePluginServerBuilder
            .FromPipeName(args[0])
            .Setup(s =>
            {
                s.Hooks.On<MonsterAggroEvent>().Use<GuardianKernel>();
                s.Subscriptions.On<AttackEvent>().Use<RetaliationKernel>();
            })
            .Build();
        await server.StartAsync();

        // (2) Tune live settings — strongly typed, one atomic IPC batch under the hood.
        await server.Get<GuardianKernel>()
            .Set(k => k.CalmStrength, 35)
            .Set(k => k.AggroRange, 6)
            .ApplyAsync(atomic: true);

        Console.WriteLine("[plugin] kernels registered, settings tuned. Exiting.");
        return 0;
    }
}
```

## 3. Two ways to express logic

There are two complementary surfaces (full rationale in
[kernel-binding-model.md](kernel-binding-model.md)):

**(a) Registered kernels** — a kernel *class* recorded against a generated hook or subscription registry:

```csharp
using var server = GamePluginServerBuilder
    .FromPipeName(pipeName)
    .Setup(s => s.Hooks.On<AttackEvent>().Use<RetaliationKernel>())
    .Build();
```

**(b) Hook chains** — inline lambda pipelines for ad-hoc logic (no kernel class). **Every
`Where`/`Select`/`Run` lambda is lowered to sandboxed IR**; only `RunLocal` stays native:

```csharp
// filter -> project -> filter -> sandboxed effect (all lowered to verified IR)
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 5)                               // sandboxed
    .Select(e => e.MonsterLevel - e.PlayerLevel)               // sandboxed projection
    .Where(gap => gap >= 3)                                    // sandboxed, sees the projection
    .Run((gap, ctx) => ctx.Messages.Send("monster", "calm:" + gap)); // lowered terminal

// RunLocal = trusted native host code, NOT sandboxed (use sparingly).
server.Hooks.On<AttackEvent>()
    .RunLocal((e, ctx) => { Console.WriteLine($"observed {e.AttackerId}"); return ValueTask.CompletedTask; });
```

`server.Subscriptions` is the notification mirror of `server.Hooks`. Same `Where`/`Select`/`Run`/
`RunLocal` chain surface — the difference is intent and dispatch:

| | `server.Hooks` | `server.Subscriptions` |
|---|---|---|
| Meaning | plugin **decides** what happens | plugin is **notified** |
| Dispatch | awaited sequentially (decisions matter) | notification delivery, exceptions isolated |

```csharp
// Hooks: the plugin's decision feeds back into the simulation (chain or a registered service kernel).
server.Hooks.On<MonsterAggroEvent>()
    .Where(e => e.Distance <= 5)
    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId));

// Subscriptions: the plugin just wants to know it happened.
server.Subscriptions.On<AttackEvent>()
    .RunLocal((e, ctx) => { Telemetry.Count("attack"); return ValueTask.CompletedTask; });
```

## 4. The generated facade

The plugin author writes a `[GeneratePluginServer(Context = typeof(GamePluginContext))]` shell with
an author-declared partial context, then consumes the generated facade:
`GamePluginServerBuilder.FromPipeName(...).Setup(...).Build()`. `Setup` records hook, subscription, and
server-extension installs; `StartAsync()` opens the IPC connection, ships the generated packages, and wires
the recorded registrations. `server.Get<TKernel>().Set(...).ApplyAsync(...)` updates live settings through
the same control plane. Plugin code never calls `IGamePluginControlService` directly.

## 5. What the analyzer does (Phase C, behind the scenes)

The author never sees this — but it's *why* the code above is safe. For each kernel and each
`Where`/`Select`/`Run` chain, the source generator emits a verified-IR package:

```csharp
// GENERATED — illustrative. The author wrote GuardianKernel in plain C#.
internal static class GuardianPluginPackage
{
    public static PluginPackage Create() => /* verified DotBoxD.Kernels module: ShouldHandle + Handle */;

    [ModuleInitializer]
    internal static void Register() =>
        KernelPackageRegistry.Register(typeof(GuardianKernel), Create);  // enables auto-install (B4)
}
```

- `Where` lambdas → AND-composed into the module's `ShouldHandle`.
- `Select` → compile-time substitution into downstream lambdas (no new runtime protocol).
- `Run` terminal → the module's `Handle` (must be a single `ctx.Messages.Send`).
- `RunLocal` → left as native host code, lowered to nothing.
- Anything outside the lowerable subset → a diagnostic (`DBXK110`–`DBXK114`), so unsafe code fails the
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
// plugin Program.cs — declarative; the framework ships the IR
await server.StartAsync();
await server.Get<GuardianKernel>()
    .Set(k => k.CalmStrength, 35)
    .Set(k => k.AggroRange, 6)
    .ApplyAsync(atomic: true);
```

Same two processes, same verified-IR safety guarantee, same IPC contract underneath — but the plugin
now *registers a kernel against a server service contract*, and the framework handles shipping and
installing the verified kernel IR.
