using DotBoxD.Kernels.Game.Plugin.Authoring;

namespace DotBoxD.Kernels.Game.Plugin.Kernels;

public readonly record struct MonsterKillResult(
    string MonsterId,
    bool WasMonster,
    int Level,
    int Position,
    int HealthBefore,
    bool Killed);

/// <summary>
/// Plugin-owned batch operation grafted onto <see cref="IMonsterControl"/>. It is injected the SAME
/// <see cref="IGameWorldAccess"/> the plugin uses remotely — but because this kernel runs on the server the
/// awaited calls are local (no real IPC hop). From the dev's seat it reads exactly like the remote plugin
/// code: <c>await _world.Monsters.KillAsync(id)</c>.
///
/// <para>One class marker names the graft target once; the install id derives from the type
/// (<c>"monster-killer"</c>); the grafted method is a bare <c>[ServerExtensionMethod]</c> whose public name is
/// its own method name. No hand-written service interface, no repeated type, no stringly-typed method name.</para>
/// </summary>
[ServerExtension(typeof(IMonsterControl))]
public sealed partial class MonsterKillerKernel
{
    private readonly IGameWorldAccess _world;

    public MonsterKillerKernel(IGameWorldAccess world) => _world = world;

    [ServerExtensionMethod]   // grafted as IMonsterControl.KillMonstersAsync (name = the method's name)
    public async ValueTask<List<MonsterKillResult>> KillMonstersAsync(List<string> monsterIds, HookContext ctx)
    {
        var results = new List<MonsterKillResult>();
        foreach (var id in monsterIds)
        {
            var healthBefore = await _world.Entities.GetHealthAsync(id);
            var wasMonster = await _world.Monsters.IsMonsterAsync(id);
            var level = await _world.Entities.GetLevelAsync(id);
            var position = await _world.Entities.GetPositionAsync(id);
            var killed = wasMonster && healthBefore > 0 && await _world.Monsters.KillAsync(id);
            results.Add(new MonsterKillResult(id, wasMonster, level, position, healthBefore, killed));
        }

        return results;
    }
}
