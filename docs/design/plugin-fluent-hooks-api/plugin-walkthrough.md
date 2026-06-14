# Plugin process — what the code looks like after the plan

Companion to [plan.md](plan.md). Server side: [server-walkthrough.md](server-walkthrough.md).

This shows the **end-state code** for the untrusted plugin process (`DotBoxd.Kernels.Game.Plugin`) once
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

Two key ideas this process is built around:

1. **The plugin never trusts itself.** Kernels and sandboxed lambdas are *lowered to verified IR*
   by the analyzer at build time. The server receives IR, not source.
2. **The plugin talks to the server with the same fluent API the server exposes locally.** The
   plugin holds a `RemotePluginServer` shim that *looks* like a `PluginServer` but forwards over IPC.

---

## The events it authors against (shared)

These records live in `DotBoxd.Kernels.Game.Server.Abstractions`, referenced by both processes. The plugin
writes kernels against them. **No event adapter is needed** — the framework infers the sandbox shape
from the record's properties via a convention adapter (`e_<PropertyName>`, CLR→sandbox type mapping).
Optional property attributes (`[OpaqueId]`, `[SandboxParam]`, `[SandboxIgnore]`) cover the cases
convention can't see. See ownership-auth-and-policy.md §3.

```csharp
namespace DotBoxd.Kernels.Game.Server.Abstractions;

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
namespace DotBoxd.Kernels.Game.Plugin;

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
namespace DotBoxd.Kernels.Game.Plugin;

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
namespace DotBoxd.Kernels.Game.Plugin;

using DotBoxd.Kernels.Game.Plugin.Client;
using DotBoxd.Kernels.Transport.Ipc;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            await Console.Error.WriteLineAsync("Usage: DotBoxd.Kernels.Game.Plugin <named-pipe-name>");
            return 1;
        }

        // (1) Connect to the server's control plane and wrap it in a server-shaped shim.
        await using var connection =
            await DotBoxdDotBoxdRpcMessagePackIpc.ConnectNamedPipeAsync(args[0]);
        var server = new RemotePluginServer(connection.Get<IGamePluginControlService>());

        // (2) Register each kernel as the implementation of a server service contract. Register
        //     ships + installs the kernel's verified IR for us — no Export, no InstallPluginAsync.
        await server.Kernels.Register<IMonsterAggroService, GuardianKernel>();
        await server.Kernels.Register<IAttackService, RetaliationKernel>();

        // (3) Tune live settings — strongly typed, one atomic IPC batch under the hood.
        await server.Kernels.Get<GuardianKernel>()
            .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true);

        Console.WriteLine("[plugin] kernels registered, settings tuned. Exiting.");
        return 0;
    }
}
```

## 3. Two ways to express logic

There are two complementary surfaces (full rationale in
[kernel-binding-model.md](kernel-binding-model.md)):

**(a) Service kernels** — a kernel *class* registered against a contract, optionally gated by a fluent,
lowered `Where` ("for which user does it apply?"):

```csharp
await server.Kernels.Register<IAttackService, RetaliationKernel>()
    .Where((e, ctx) => e.Damage >= 10);   // lowered to verified IR; runs before the kernel's gate
```

**(b) Hook chains** — inline lambda pipelines for ad-hoc logic (no kernel class). **Every
`Where`/`Select`/`InvokeKernel` lambda is lowered to sandboxed IR**; only `InvokeLocal` stays native:

```csharp
// filter -> project -> filter -> sandboxed effect (all lowered to verified IR)
server.Hooks.On<MonsterAggroEvent>()
    .Where((e, ctx)  => e.Distance <= 5)                       // sandboxed
    .Select((e, ctx) => e.MonsterLevel - e.PlayerLevel)        // sandboxed projection
    .Where((gap, ctx) => gap >= 3)                             // sandboxed, sees the projection
    .InvokeKernel((gap, ctx) => ctx.Messages.Send("monster", "calm:" + gap)); // lowered terminal

// InvokeLocal = trusted native host code, NOT sandboxed (use sparingly).
server.Hooks.On<AttackEvent>()
    .InvokeLocal((e, ctx) => { Console.WriteLine($"observed {e.AttackerId}"); return ValueTask.CompletedTask; });
```

`server.Events` is the fire-and-forget mirror of `server.Hooks`. Same `Where/Select/InvokeKernel/
InvokeLocal` chain surface — the difference is intent and dispatch:

| | `server.Hooks` | `server.Events` |
|---|---|---|
| Meaning | plugin **decides** what happens | plugin is **notified** |
| Dispatch | awaited sequentially (decisions matter) | fire-and-forget, exceptions isolated |

```csharp
// Hooks: the plugin's decision feeds back into the simulation (chain or a registered service kernel).
server.Hooks.On<MonsterAggroEvent>()
    .Where((e, ctx) => e.Distance <= 5)
    .InvokeKernel((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId));

// Events: the plugin just wants to know it happened.
server.Events.On<AttackEvent>()
    .InvokeLocal((e, ctx) => { Telemetry.Count("attack"); return ValueTask.CompletedTask; });
```

## 4. The shim — `RemotePluginServer` (Phase A)

> ⚠️ **DESIGN SKETCH, not compilable as-is.** The block below names Phase-B types that do not exist yet
> (`KernelPackageRegistry`, `LiveSettingExtractor`, a `Func<...>` placeholder) and uses a `GetAwaiter`
> struct builder the review rejected. The shipped form uses a **`Task`-returning** `Register(where:)`,
> the real `LiveKernelValueFactory.ExtractSettings`, and a generated draft factory (no `new()`
> constraint). See [implementation-plan.md](implementation-plan.md). Read this for *shape*, not literal code.

A small example-local class that gives the plugin a `PluginServer`-shaped surface while forwarding
over the unchanged IPC contract (`IGamePluginControlService`, defined in
[server-walkthrough.md](server-walkthrough.md)). `Register<TService, TKernel>()` is a real round-trip:
resolve the generated package → `InstallPluginAsync`.

```csharp
namespace DotBoxd.Kernels.Game.Plugin.Client;

using DotBoxd.Kernels.Game.Server.Abstractions;

/// <summary>
/// Server-shaped facade over the IPC control service. Lets plugin code read
/// `server.Kernels.Register<TService, TKernel>()`, `server.Kernels.Get<TKernel>().SetValuesAsync(..)`,
/// and the `server.Hooks.On<>()` chain without ever touching IGamePluginControlService directly.
/// </summary>
internal sealed class RemotePluginServer
{
    private readonly IGamePluginControlService _control;

    public RemotePluginServer(IGamePluginControlService control)
    {
        _control = control;
        Hooks   = new RemoteHookRegistry(control);    // On<TEvent>().Where/Select/InvokeKernel/InvokeLocal
        Kernels = new RemoteKernelControl(control);
    }

    public RemoteHookRegistry  Hooks   { get; }
    public RemoteKernelControl Kernels { get; }
}

internal sealed class RemoteKernelControl
{
    private readonly IGamePluginControlService _control;
    public RemoteKernelControl(IGamePluginControlService control) => _control = control;

    // Register a kernel as an implementation of a server service contract. Returns an awaitable
    // builder so an optional lowered .Where(..) gate can be chained (see kernel-binding-model.md §3).
    public ServiceKernelRegistration<TService> Register<TService, TKernel>()
        where TService : class
        where TKernel  : class, TService
        => new(_control, KernelPackageRegistry.GetByKernelType<TKernel>()); // generated self-registration

    // Typed, unambiguous on the plugin side: TKernel -> [Plugin] id.
    public RemoteKernelHandle<TKernel> Get<TKernel>() where TKernel : class, new()
        => new(_control, KernelTypeMetadata.PluginId(typeof(TKernel)));

    public RemoteKernelHandle Get(string pluginId) => new(_control, pluginId);
}

internal readonly struct ServiceKernelRegistration<TService> where TService : class
{
    private readonly IGamePluginControlService _control;
    private readonly PluginPackage _package;
    public ServiceKernelRegistration(IGamePluginControlService control, PluginPackage package)
    { _control = control; _package = package; }

    // Optional lowered gate; in Phase A the analyzer has already folded it into the shipped IR, so
    // here it is a no-op marker that selects the gated package variant. (See kernel-binding-model.md.)
    public ServiceKernelRegistration<TService> Where(Func<...> gate) => this;

    public ValueTaskAwaiter<string> GetAwaiter() => ApplyAsync().GetAwaiter();   // awaitable builder
    public async ValueTask<string> ApplyAsync()
        => await _control.InstallPluginAsync(PluginPackageJsonSerializer.Export(_package));
}

// Typed settings handle: SetValuesAsync(Action<TKernel>) mutates a local draft, ships the diff.
internal sealed class RemoteKernelHandle<TKernel> where TKernel : class, new()
{
    private readonly IGamePluginControlService _control;
    private readonly string _pluginId;
    public RemoteKernelHandle(IGamePluginControlService control, string pluginId)
    { _control = control; _pluginId = pluginId; }

    public ValueTask SetValuesAsync(Action<TKernel> set, bool atomic = false)
    {
        var draft = new TKernel();
        set(draft);                                                   // author sets values on a draft
        var updates = LiveSettingExtractor.Extract(draft);           // [LiveSetting] props -> updates
        return _control.UpdateSettingsAsync(_pluginId, updates, atomic);
    }
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
    public static PluginPackage Create() => /* verified DotBoxd.Kernels module: ShouldHandle + Handle */;

    [ModuleInitializer]
    internal static void Register() =>
        KernelPackageRegistry.Register(typeof(GuardianKernel), Create);  // enables auto-install (B4)
}
```

- `Where` lambdas → AND-composed into the module's `ShouldHandle`.
- `Select` → compile-time substitution into downstream lambdas (no new runtime protocol).
- `InvokeKernel` terminal → the module's `Handle` (must be a single `ctx.Messages.Send`).
- `InvokeLocal` → left as native host code, lowered to nothing.
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
await server.Kernels.Register<IMonsterAggroService, GuardianKernel>();
await server.Kernels.Get<GuardianKernel>()
    .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true);
```

Same two processes, same verified-IR safety guarantee, same IPC contract underneath — but the plugin
now *registers a kernel against a server service contract*, and the framework handles shipping and
installing the verified kernel IR.
