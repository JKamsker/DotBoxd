using System.ComponentModel.DataAnnotations;

namespace DotBoxD.Kernels.Game.Plugin.Kernels;

[ServerExtension("bounty.claim")]
public sealed partial class BountyPayoutKernel
{
    private readonly IGameWorldAccess _world;

    public BountyPayoutKernel(IGameWorldAccess world) => _world = world;

    [LiveSetting]
    [Range(1, 100)]
    public int GoldPerLevel { get; set; } = 10;

    [LiveSetting]
    [Range(1, 500)]
    public int MaxBountyPerKill { get; set; } = 60;

    public async ValueTask<string> ClaimAsync(string playerId, string monsterId, HookContext ctx)
    {
        var monster = _world.Entities.Get(monsterId);
        if (await monster.GetHealthAsync() > 0)
        {
            return "denied:not-dead";
        }

        if (!await _world.Monsters.IsMonsterAsync(monsterId))
        {
            return "denied:not-monster";
        }

        if (!await _world.Gold.IsBountyClaimableAsync(monsterId))
        {
            return "denied:already-claimed";
        }

        var amount = await monster.GetLevelAsync() * GoldPerLevel;
        if (amount > MaxBountyPerKill)
        {
            amount = MaxBountyPerKill;
        }

        var granted = await _world.Gold.GrantBountyAsync(playerId, monsterId, amount);
        if (!granted)
        {
            return "denied:budget";
        }

        return "paid";
    }
}
