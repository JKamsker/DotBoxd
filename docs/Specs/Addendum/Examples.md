# Addendum Implementation Examples

The addendum is now demonstrated through one maintained runnable example:

- `samples/GameServer/Examples.GameServer.Server.Abstractions` defines the server-owned event
  contracts, plugin control-plane service contract, MessagePack DTOs, and host binding facade.
- `samples/GameServer/Examples.GameServer.Plugin` is the plugin child process. It authors
  kernels in C#, resolves the analyzer-generated packages, ships verified IR over IPC, tunes live
  settings, and invokes a server extension.
- `samples/GameServer/Examples.GameServer.Server` is the parent process. It hosts the world
  simulation, installs packages into `DotBoxD.Plugins`, grants least-privilege policies, binds
  host-world capabilities, and unloads kernels when the plugin connection ends.

The old topic-specific examples were removed to keep one high-quality sample. Their historical source
is preserved by the `examples-before-prune-2026-06-15` tag, and the feature gaps left by removing them
are tracked in [`docs/examples/coverage-gaps.md`](../../examples/coverage-gaps.md).

## Recommended Walkthrough

The public docs should lead with the kernel-class authoring model, then show how the server installs
and runs the lowered package without loading arbitrary plugin code.

### 1. Implement An Event Kernel

Use event kernels when a plugin needs a server-side filter and an approved action path. The GameServer
sample starts with named service contracts that extend `IEventKernel<TEvent>`:

```csharp
public interface IMonsterAggroService : IEventKernel<MonsterAggroEvent>
{
}

public interface IAttackService : IEventKernel<AttackEvent>
{
}
```

The plugin implements those contracts as ordinary C# classes. The analyzer lowers the supported subset
into package-backed verified IR.

```csharp
[Plugin("retaliation")]
public sealed partial class RetaliationKernel : IAttackService
{
    [LiveSetting]
    [Range(0, 10_000)]
    public int MinDamage { get; set; } = 5;

    [LiveSetting]
    [Range(0, 100)]
    public int MinAttackerLevel { get; set; } = 5;

    public bool ShouldHandle(AttackEvent e, HookContext ctx)
        => e.Damage >= MinDamage &&
           e.AttackerLevel >= MinAttackerLevel;

    public void Handle(AttackEvent e, HookContext ctx)
        => ctx.Messages.Send(e.AttackerId, "taunt:" + e.TargetId);
}
```

`ShouldHandle` is the server-side filter. `Handle` can only perform actions exposed by the approved
context facade. In GameServer, `ctx.Messages.Send(...)` writes an example-defined command DSL that the
host validates before mutating the world.

### 2. Ship Verified IR Over IPC

The plugin process connects to the server's `IGamePluginControlService` over MessagePack IPC. Its
example-local `RemotePluginServer` gives plugin authors a server-shaped API while forwarding every
operation over the control-plane contract:

```csharp
var guardianId = await server.Kernels.Register<IMonsterAggroService, GuardianKernel>();
var retaliationId = await server.Kernels.Register<IAttackService, RetaliationKernel>();
```

`Register<TService, TKernel>()` resolves the analyzer-generated package with
`KernelPackageRegistry.Resolve<TKernel>()`, exports it with `PluginPackageJsonSerializer.Export(...)`,
and sends the JSON package to `InstallPluginAsync`. The parent process imports that package, validates
the manifest and IR, applies the server policy, and wires the hook for the declared event
subscription. The server never compiles or loads the plugin assembly.

### 3. Update Live Settings

Kernel properties annotated with `[LiveSetting]` become live manifest state. The GameServer plugin
tunes those values after install:

```csharp
await server.Kernels.Get<GuardianKernel>()
    .SetValuesAsync(k => { k.CalmStrength = "35"; k.AggroRange = 6; }, atomic: true);
```

The example helper reflects the live-setting properties on a local draft object, converts the values
to the control-plane DTO, and calls `UpdateSettingsAsync(..., atomic: true)`. The parent process
validates and commits the batch before future hook executions observe it.

### 4. Gate Host Bindings With Capabilities

`GuardianKernel` reads world state through an approved host facade:

```csharp
ctx.Host<IGameWorldAccess>().GetHealth(e.MonsterId)
```

The server backs that method with a binding such as `host.world.getHealth`, declares a capability such
as `game.world.monster.read.health`, and grants only the wildcard needed by kernels whose generated
manifest actually requested a matching capability. `RetaliationKernel` does not read world state, so
it does not receive the world-read grant.

The GameServer host also emits per-resource audit events from those bindings, uses deterministic cost
models, and sets fuel and host-call budgets in `ServerPolicy`.

### 5. Use Server Extensions For Pushdown

GameServer also shows the pushdown shape: the plugin supplies a batch operation that runs on the
server next to fine-grained host bindings.

```csharp
[ServerExtension("monster-killer", typeof(IMonsterKillerService))]
public sealed partial class MonsterKillerKernel
{
    private readonly IGameWorldAccess _world;

    public MonsterKillerKernel(IGameWorldAccess world) => _world = world;

    [ServerExtensionMethod(typeof(RemoteMonsterControl), "KillMonstersAsync")]
    public List<MonsterKillResult> KillMonsters(List<string> monsterIds, HookContext ctx)
    {
        var results = new List<MonsterKillResult>();
        foreach (var id in monsterIds)
        {
            var wasMonster = _world.IsMonster(id);
            var killed = wasMonster && _world.KillMonster(id);
            results.Add(new MonsterKillResult(id, wasMonster, _world.GetLevel(id), _world.GetPosition(id), 0, killed));
        }

        return results;
    }
}
```

The plugin invokes the generated client once; the server executes the generated verified IR and returns
the result list. The server still exposes only reviewed fine-grained bindings, and the server-extension
package receives write capability only because its manifest declares the kill binding.

### 6. Run The Example

```powershell
dotnet run --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

The server prints a baseline phase, launches the plugin child process, installs event kernels and the
server-extension package, runs a with-plugin phase, then disconnects the plugin and shows that its kernels
were unloaded with the connection.
