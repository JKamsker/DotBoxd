namespace DotBoxD.Kernels.Game.Client.Abstractions;

[DotBoxDService]
public interface IGameClientAccess
{
    IClientHud Ui { get; }

    IClientFx Fx { get; }

    IGameServerRelay Server { get; }
}

[DotBoxDService]
public interface IClientHud
{
    [HostCapability("game.client.ui.write", HostBindingEffect.HostStateWrite)]
    ValueTask WriteLineAsync(string channel, string text);
}

[DotBoxDService]
public interface IClientFx
{
    [HostCapability("game.client.fx.play", HostBindingEffect.HostStateWrite)]
    ValueTask PlayAsync(string effectId, string targetId);
}

[DotBoxDService]
public interface IGameServerRelay
{
    [HostCapability("game.client.server.call", HostBindingEffect.HostStateWrite | HostBindingEffect.Allocates)]
    ValueTask<string> CallAsync(string operation, string payload);
}
