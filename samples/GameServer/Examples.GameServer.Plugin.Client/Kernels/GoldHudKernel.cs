using DotBoxD.Kernels.Game.Client.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Client.Kernels;

[EventKernel]
public sealed partial class GoldHudKernel : IEventKernel<ClientGoldChangedEvent>
{
    public bool ShouldHandle(ClientGoldChangedEvent e, HookContext context)
        => e.EntityId == "player-1";

    public void Handle(ClientGoldChangedEvent e, HookContext context)
        => context.Messages.Send("hud", $"gold:{e.Balance}");
}
