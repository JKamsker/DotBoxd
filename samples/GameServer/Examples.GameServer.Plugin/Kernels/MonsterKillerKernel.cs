namespace DotBoxD.Kernels.Game.Plugin.Kernels;

public readonly record struct MonsterKillResult(
    string MonsterId,
    bool WasMonster,
    int Level,
    int Position,
    int HealthBefore,
    bool Killed);

/// <summary>
/// Plugin-owned batch operation grafted onto the <see cref="IMonsterControl"/> collection (contrast
/// <see cref="BlinkKernel"/>, which grafts onto a single <see cref="IMonster"/> handle). It is injected the
/// SAME <see cref="IGameWorldAccess"/> the plugin uses remotely — but because this kernel runs on the server
/// the awaited calls are local (no real IPC hop).
///
/// <para>Each id is turned into a scoped handle once via <c>_world.Monsters.Get(id)</c>, so the per-entity
/// reads/writes below omit the id: <c>handle.GetHealthAsync()</c>, <c>handle.KillAsync()</c>.</para>
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
            var monster = _world.Monsters.Get(id);            // scoped handle — id captured once
            var healthBefore = await monster.GetHealthAsync();
            var wasMonster = await _world.Monsters.IsMonsterAsync(id);
            var level = await monster.GetLevelAsync();
            var position = await monster.GetPositionAsync();
            var killed = wasMonster && healthBefore > 0 && await monster.KillAsync();
            results.Add(new MonsterKillResult(id, wasMonster, level, position, healthBefore, killed));
        }

        return results;
    }
}
