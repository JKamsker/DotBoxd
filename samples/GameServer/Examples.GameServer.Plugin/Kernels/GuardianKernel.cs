using System.ComponentModel.DataAnnotations;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Kernels;

/// <summary>
/// Untrusted plugin event kernel. Calms a monster that is about to bully a low-level player. Authored as plain
/// C#, lowered to verified DotBoxD.Kernels by the source generator, and shipped to the server as opaque IR.
/// The install id derives from the type name (<c>"guardian"</c>) — nothing hand-typed.
/// </summary>
[Plugin]
public sealed partial class GuardianKernel : IMonsterAggroService
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

    // The kernel emits this into the `calm:<player>:<strength>` host message. Kernel IR supports
    // deterministic invariant int-to-string conversion for interpolation, so the setting can remain numeric.
    [LiveSetting]
    [Range(0, 50)]
    public int CalmStrength { get; set; } = 20;

    // Event hooks stay synchronous. Async world reads live in server extensions and InvokeAsync; aggro events
    // are only published for live monsters, so this gate can read event data directly.
    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx)
        => IsBullyingLowLevelPlayer(
            e.MonsterLevel,
            e.PlayerLevel,
            e.Distance,
            LevelGap,
            AggroRange,
            ProtectMaxLevel);

    public void Handle(MonsterAggroEvent e, HookContext ctx)
        => ctx.Messages.Send(e.MonsterId, $"calm:{e.PlayerId}:{CalmStrength}");

    /// <summary>
    /// Reusable, unit-testable gate factored out with <c>[KernelMethod]</c>: the generator inlines this body
    /// into <see cref="ShouldHandle"/> after the live-setting reads have been lowered at the call site.
    /// </summary>
    [KernelMethod]
    public static bool IsBullyingLowLevelPlayer(
        int monsterLevel,
        int playerLevel,
        int distance,
        int levelGap,
        int aggroRange,
        int protectMaxLevel)
        => monsterLevel - playerLevel >= levelGap &&
           distance <= aggroRange &&
           playerLevel <= protectMaxLevel;
}
