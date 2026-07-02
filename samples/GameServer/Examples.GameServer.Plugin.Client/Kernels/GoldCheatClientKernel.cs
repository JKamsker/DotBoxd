namespace DotBoxD.Kernels.Game.Plugin.Client.Kernels;

[ServerExtension("gold.cheat.client")]
public sealed partial class GoldCheatClientKernel
{
    private readonly IGameClientAccess _client;

    public GoldCheatClientKernel(IGameClientAccess client) => _client = client;

    public async ValueTask<string> CheatAsync(string monsterId, HookContext ctx)
        => await _client.Server.CallAsync("gold.grant", monsterId);
}
