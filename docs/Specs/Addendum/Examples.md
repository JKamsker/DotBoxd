# Addendum Implementation Examples

The addendum is demonstrated through one maintained runnable example:

- `samples/GameServer/Examples.GameServer.Server.Abstractions` defines the server-owned event
  contracts, gold ledger facade, read-only world view, MessagePack DTOs, and plugin/client control
  contracts.
- `samples/GameServer/Examples.GameServer.Client.Abstractions` defines the restricted client-half SDK:
  HUD writes, effects, client-local events, and the approved server relay surface.
- `samples/GameServer/Examples.GameServer.Plugin` authors server-half kernels and exports package JSON
  into `samples/GameServer/plugins`.
- `samples/GameServer/Examples.GameServer.Plugin.Client` authors client-half kernels and exports package
  JSON plus client assets into the same bundle root.
- `samples/GameServer/Examples.GameServer.Server` is the trusted authoritative process. It hosts the
  world and gold economy, loads server bundle parts from disk, checks the operator allow-list, starts a
  TCP loopback listener, and authorizes relay calls before invoking server extensions.
- `samples/GameServer/Examples.GameServer.Client` is the trusted vendor client process. It connects over
  TCP, exposes only `IGameWorldView`, installs client bundle parts into its own in-process sandbox, and
  renders HUD output.

The old topic-specific examples were removed to keep one high-quality sample. Their historical source
is preserved by the `examples-before-prune-2026-06-15` tag, and the feature gaps left by removing them
are tracked in [`docs-site/src/content/docs/examples/coverage-gaps.md`](../../../docs-site/src/content/docs/examples/coverage-gaps.md).

## Recommended Walkthrough

The public docs should lead with package-backed kernel authoring, then show how the trusted server and
trusted client install different halves under different policies.

### 1. Author Server-Side Kernels

Use event kernels when a plugin needs a server-side filter and approved action path. The GameServer
server-half package includes:

```csharp
[EventKernel]
public sealed partial class GuardianKernel : IMonsterAggroService
{
    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx)
        => e.MonsterLevel - e.PlayerLevel >= LevelGap &&
           e.Distance <= AggroRange &&
           e.PlayerLevel <= ProtectMaxLevel;

    public void Handle(MonsterAggroEvent e, HookContext ctx)
        => ctx.Messages.Send(e.MonsterId, $"calm:{e.PlayerId}:{CalmStrength}");
}
```

`ShouldHandle` is lowered to server-side verified IR. `Handle` can only use host actions exposed by the
approved context facade. In GameServer, `ctx.Messages.Send(...)` writes an example-defined command DSL
that the host validates before mutating the world.

### 2. Author A Privileged Server Extension

The `BountyPayoutKernel` server half is the pushdown example. It runs on the server, reads authoritative
monster state, applies plugin-authored rules, and asks the server-owned ledger to grant a bounded bounty:

```csharp
[ServerExtension("bounty.claim")]
public sealed partial class BountyPayoutKernel
{
    private readonly IGameWorldAccess _world;

    public BountyPayoutKernel(IGameWorldAccess world) => _world = world;

    public async ValueTask<string> ClaimAsync(string playerId, string monsterId, HookContext ctx)
    {
        var monster = _world.Entities.Get(monsterId);
        if (await monster.GetHealthAsync() > 0)
        {
            return "denied:not-dead";
        }

        if (!await _world.Gold.IsBountyClaimableAsync(monsterId))
        {
            return "denied:already-claimed";
        }

        var granted = await _world.Gold.GrantBountyAsync(playerId, monsterId, MaxBountyPerKill);
        return granted ? "paid" : "denied:budget";
    }
}
```

The kernel is stateless. Durable balances, claims, treasury, and per-tick budget live in
`Simulation/GameEconomy.cs`, so duplicate claims and budget overrun are refused even if a plugin asks
twice.

### 3. Export Disk Bundles

The authoring projects export analyzer-generated packages at build time:

```text
samples/GameServer/plugins/
  guardian/server/hooks/guardian.json
  guardian/server/subscriptions/retaliation.json
  bounty-hunter/server/extensions/bounty-payout.json
  bounty-hunter/client/extensions/bounty-claim.json
  bounty-hunter/client/hooks/monster-death-fx.json
  bounty-hunter/client/subscriptions/gold-hud.json
  bounty-hunter/client/assets/skull.anim.txt
  gold-cheat/server/extensions/gold-cheat.json
  gold-cheat/client/extensions/gold-cheat.json
```

The exporter uses `KernelPackageRegistry.Resolve(type)` and `PluginPackageJsonSerializer.Export(...)`.
The host still imports the JSON and derives capabilities from the IR; the folder layout is delivery
metadata, not a trusted security claim.

### 4. Apply The Operator Allow-List

The server loads only `server/` parts. For each package it computes required capabilities and intersects
them with `PluginCatalog/OperatorAllowList.cs`.

`bounty-hunter` may install `bounty.claim` because the operator grants the needed world-read and gold
grant capabilities. `gold-cheat` also asks for gold write, but that bundle id is not approved, so the
server prints a deny verdict and never installs it.

### 5. Keep The Client Sandbox Narrow

The client loads only `client/` parts into its own `PluginServer`. Its SDK exposes:

- `game.client.ui.write`
- `game.client.fx.play`
- `game.client.server.call`

It does not reference the server gold ledger and does not register gold bindings. The client-side
`gold-cheat` half is denied, and the relay allow-list rejects unknown operations before they reach the
server.

### 6. Relay Through The Server Control Plane

The server provides `IGameClientControlService` over a loopback TCP transport. The client receives
`IGameWorldView`, not `IGameWorldAccess`, so it can read a snapshot and balance but cannot mutate the
world directly.

`CallPluginOperationAsync` is the only privileged relay. It enforces payload size, rate limit,
operator-registered operation id, and server-side player identity injection before invoking the installed
server extension.

### 7. Run The Example

```powershell
dotnet run --project samples/GameServer/Examples.GameServer.Server/Examples.GameServer.Server.csproj
```

The server prints bundle install verdicts, runs a deterministic baseline, starts the TCP listener,
launches the client, shows the client sandbox deny path, processes bounty claims, retunes a live
setting, and prints the final balances and claimed monsters.
