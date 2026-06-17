using System.ComponentModel.DataAnnotations;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Kernels;

/// <summary>
/// Untrusted plugin event kernel. Taunts a strong attacker so it switches away from the player it is hitting.
/// Lowered to verified DotBoxD.Kernels and shipped as opaque IR over IPC. Install id derives from the type
/// name (<c>"retaliation"</c>).
/// </summary>
[EventKernel]
public sealed partial class RetaliationKernel : IAttackService
{
    [LiveSetting]
    [Range(0, 10_000)]
    public int MinDamage { get; set; } = 5;

    [LiveSetting]
    [Range(0, 100)]
    public int MinAttackerLevel { get; set; } = 5;

    public bool ShouldHandle(AttackEvent e, HookContext ctx)
        => e.Damage >= MinDamage &&
           e.AttackerLevel >= MinAttackerLevel;

    public void Handle(AttackEvent e, HookContext ctx)
        => ctx.Messages.Send(e.AttackerId, "taunt:" + e.TargetId);
}
