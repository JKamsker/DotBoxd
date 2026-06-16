using DotBoxD.Kernels.Game.Plugin.Authoring;

namespace DotBoxD.Kernels.Game.Plugin.Kernels;

/// <summary>
/// Instance-scoped server extension grafted onto the MONSTER HANDLE <see cref="IMonster"/> (not the control).
/// The kernel is constructed <b>scoped to the specific monster the caller addressed</b>: when the plugin calls
/// <c>world.Monsters.Get("monster-4").BlinkBehindAsync(...)</c>, the <c>Get(id)</c> captures the id, the server
/// resolves that monster and injects it, and the body uses <c>_monster</c> directly — the id is never
/// re-specified.
///
/// <para><b>Injection options.</b> An instance-scoped kernel may take, in any combination: the <b>scoped
/// instance</b> (<see cref="IMonster"/> — the addressed monster), the <b>root world</b>
/// (<see cref="IGameWorldAccess"/> — for reads beyond this monster), or both. This kernel injects both: the
/// scoped monster is the write target; the world looks up the player it should blink behind.</para>
///
/// <para>Contrast <see cref="MonsterKillerKernel"/>, which grafts onto the <c>IMonsterControl</c> collection
/// (a batch over a list of ids). Graft onto a control for collection ops; graft onto a handle for per-entity
/// ops with the id captured.</para>
/// </summary>
[ServerExtension(typeof(IMonster))]
public sealed partial class BlinkKernel
{
    private readonly IMonster _monster;        // the scoped instance — id already captured by Monsters.Get(id)
    private readonly IGameWorldAccess _world;  // the root world — for reads beyond this one monster

    public BlinkKernel(IMonster monster, IGameWorldAccess world)
    {
        _monster = monster;
        _world = world;
    }

    [ServerExtensionMethod]   // grafted as IMonster.BlinkBehindAsync (name = the method's name)
    public async ValueTask<int> BlinkBehindAsync(string playerId, HookContext ctx)
    {
        // Root-world read (the player) + scoped-instance read/write (this monster) — no monster id passed.
        var playerPosition = await _world.Entities.Get(playerId).GetPositionAsync();
        var threat = await _monster.GetThreatAsync();
        var target = playerPosition - 1 - (threat > 7 ? 1 : 0);
        await _monster.TeleportToAsync(target);
        return target;
    }
}
