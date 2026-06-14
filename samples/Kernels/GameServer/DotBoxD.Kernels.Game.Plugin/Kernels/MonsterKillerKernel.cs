namespace DotBoxD.Kernels.Game.Plugin;

public interface IMonsterKillerService
{
    ValueTask<List<MonsterKillResult>> KillMonstersAsync(List<string> monsterIds);
}

public readonly record struct MonsterKillResult(
    string MonsterId,
    bool WasMonster,
    int Level,
    int Position,
    int HealthBefore,
    bool Killed);

/// <summary>
/// Plugin-owned batch operation. The class and service contract live in the plugin assembly, but the
/// generated verified IR is installed and executed on the server through the kernel RPC IPC bridge.
/// </summary>
[KernelRpcService("monster-killer", typeof(IMonsterKillerService))]
public sealed partial class MonsterKillerKernel
{
    private readonly IGameWorldAccess _world;

    public MonsterKillerKernel(IGameWorldAccess world) => _world = world;

    public List<MonsterKillResult> KillMonsters(List<string> monsterIds, HookContext ctx)
    {
        var results = new List<MonsterKillResult>();
        foreach (var id in monsterIds)
        {
            var healthBefore = _world.GetHealth(id);
            var wasMonster = _world.IsMonster(id);
            var level = _world.GetLevel(id);
            var position = _world.GetPosition(id);
            var killed = false;
            if (wasMonster)
            {
                if (healthBefore > 0)
                {
                    killed = _world.KillMonster(id);
                }
            }

            results.Add(new MonsterKillResult(id, wasMonster, level, position, healthBefore, killed));
        }

        return results;
    }
}
