namespace DotBoxD.Kernels.Game.Plugin.Client.Kernels;

[ServerExtension("bounty.claim.client")]
public sealed partial class BountyClaimKernel
{
    private readonly IGameClientAccess _client;

    public BountyClaimKernel(IGameClientAccess client) => _client = client;

    public async ValueTask<string> ClaimAsync(string monsterId, HookContext ctx)
    {
        var receipt = await _client.Server.CallAsync("bounty.claim", monsterId);
        await _client.Ui.WriteLineAsync("hud", "bounty: " + receipt);
        return receipt;
    }
}
