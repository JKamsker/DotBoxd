using System.ComponentModel.DataAnnotations;
using DotBoxD.Kernels.Game.Plugin.Authoring;
using DotBoxD.Kernels.Game.Server.Abstractions.Events;

namespace DotBoxD.Kernels.Game.Plugin.Kernels;

/// <summary>
/// Untrusted plugin event kernel. Calms a monster that is about to bully a low-level player. Authored as plain
/// C#, lowered to verified DotBoxD.Kernels by the source generator, and shipped to the server as opaque IR.
/// The install id derives from the type name (<c>"guardian"</c>) — nothing hand-typed.
/// </summary>
[EventKernel]
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

    // Numeric like its siblings, so it keeps the [Range] guard and compile-time typing (the server clamps at
    // 50). Live settings stringify on the wire regardless of C# type, so this round-trips identically.
    [LiveSetting]
    [Range(0, 50)]
    public int CalmStrength { get; set; } = 20;

    // Event hooks stay synchronous. Async world reads live in server extensions and InvokeAsync; aggro events
    // are only published for live monsters, so this gate can read event data directly.
    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx)
        => IsBullyingLowLevelPlayer(e.MonsterLevel, e.PlayerLevel, e.Distance);

    public void Handle(MonsterAggroEvent e, HookContext ctx)
        => ctx.Messages.Send(e.MonsterId, $"calm:{e.PlayerId}:{CalmStrength}");

    /// <summary>
    /// Reusable, unit-testable gate factored out with <c>[KernelMethod]</c>: the generator inlines this body
    /// into <see cref="ShouldHandle"/> and resolves the <c>[LiveSetting]</c> reads (<c>LevelGap</c> etc.)
    /// directly, so the author passes only event data — no need to thread every live setting as an argument.
    /// </summary>
    [KernelMethod]
    public bool IsBullyingLowLevelPlayer(int monsterLevel, int playerLevel, int distance)
        => monsterLevel - playerLevel >= LevelGap &&
           distance <= AggroRange &&
           playerLevel <= ProtectMaxLevel;
}
