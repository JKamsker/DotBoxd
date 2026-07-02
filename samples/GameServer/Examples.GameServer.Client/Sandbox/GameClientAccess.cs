using DotBoxD.Kernels.Game.Client.Rendering;

namespace DotBoxD.Kernels.Game.Client.Sandbox;

internal sealed class GameClientAccess : IGameClientAccess
{
    private static readonly IReadOnlySet<string> AllowedServerOperations =
        new HashSet<string>(["bounty.claim"], StringComparer.Ordinal);

    public GameClientAccess(ConsoleHudRenderer renderer, IGameClientControlService control)
    {
        Ui = new ClientHud(renderer);
        Fx = new ClientFx(renderer);
        Server = new GameServerRelay(control, AllowedServerOperations);
    }

    public IClientHud Ui { get; }

    public IClientFx Fx { get; }

    public IGameServerRelay Server { get; }
}

internal sealed class ClientHud(ConsoleHudRenderer renderer) : IClientHud
{
    [HostCapability("game.client.ui.write", HostBindingEffect.HostStateWrite)]
    public ValueTask WriteLineAsync(string channel, string text)
    {
        renderer.Write(channel, text);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ClientFx(ConsoleHudRenderer renderer) : IClientFx
{
    [HostCapability("game.client.fx.play", HostBindingEffect.HostStateWrite)]
    public ValueTask PlayAsync(string effectId, string targetId)
    {
        renderer.Write("fx", effectId + ":" + targetId);
        return ValueTask.CompletedTask;
    }
}

internal sealed class GameServerRelay(
    IGameClientControlService control,
    IReadOnlySet<string> allowedOperations) : IGameServerRelay
{
    [HostCapability("game.client.server.call", HostBindingEffect.HostStateWrite | HostBindingEffect.Allocates)]
    public async ValueTask<string> CallAsync(string operation, string payload)
    {
        if (!allowedOperations.Contains(operation))
        {
            return "denied:client-operation";
        }

        return await control.CallPluginOperationAsync(operation, payload).ConfigureAwait(false);
    }
}
