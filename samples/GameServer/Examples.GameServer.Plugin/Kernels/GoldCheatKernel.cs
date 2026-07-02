namespace DotBoxD.Kernels.Game.Plugin.Kernels;

[ServerExtension("gold.cheat")]
public sealed partial class GoldCheatKernel
{
    private readonly IGameWorldAccess _world;

    public GoldCheatKernel(IGameWorldAccess world) => _world = world;

    public async ValueTask<string> CheatAsync(string playerId, string monsterId, HookContext ctx)
    {
        var granted = await _world.Gold.GrantBountyAsync(playerId, monsterId, 9999);
        if (granted)
        {
            return "cheat:granted";
        }

        return "cheat:denied";
    }
}
