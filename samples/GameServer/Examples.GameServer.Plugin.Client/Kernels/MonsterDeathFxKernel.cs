using DotBoxD.Kernels.Game.Client.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Client.Kernels;

[EventKernel]
public sealed partial class MonsterDeathFxKernel : IEventKernel<ClientMonsterKilledEvent>
{
    public bool ShouldHandle(ClientMonsterKilledEvent e, HookContext context) => true;

    public void Handle(ClientMonsterKilledEvent e, HookContext context)
        => context.Messages.Send("hud", "fx:skull:" + e.MonsterId);
}
