namespace DotBoxd.Kernels.Game.Plugin;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Untrusted plugin kernel. Taunts a strong attacker so it switches away from the player it is
/// hitting. Lowered to verified DotBoxd.Kernels and shipped as opaque IR over IPC.
/// </summary>
[Plugin("retaliation")]
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
