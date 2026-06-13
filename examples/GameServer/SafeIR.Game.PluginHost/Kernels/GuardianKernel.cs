namespace SafeIR.Game.PluginHost;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Untrusted plugin kernel. Calms a monster that is about to bully a low-level player. The kernel is
/// authored as plain C#, lowered to verified SafeIR by the source generator, and shipped to the
/// server as opaque IR — the server never compiles this source.
/// </summary>
[Plugin("guardian")]
public sealed partial class GuardianKernel : IEventKernel<MonsterAggroEvent>
{
    [LiveSetting]
    [Range(0, 100)]
    public int LevelGap { get; set; } = 3;

    [LiveSetting]
    [Range(0, 100)]
    public int AggroRange { get; set; } = 5;

    [LiveSetting]
    [Range(0, 100)]
    public int ProtectMaxLevel { get; set; } = 5;

    [LiveSetting]
    public string CalmStrength { get; set; } = "20";

    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx)
        => e.MonsterLevel - e.PlayerLevel >= LevelGap &&
           e.Distance <= AggroRange &&
           e.PlayerLevel <= ProtectMaxLevel;

    public void Handle(MonsterAggroEvent e, HookContext ctx)
        => ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId + ":" + CalmStrength);
}
