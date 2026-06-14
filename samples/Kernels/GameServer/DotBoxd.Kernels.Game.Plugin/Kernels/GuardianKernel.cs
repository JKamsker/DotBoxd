namespace DotBoxd.Kernels.Game.Plugin;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Untrusted plugin kernel. Calms a monster that is about to bully a low-level player. The kernel is
/// authored as plain C#, lowered to verified DotBoxd.Kernels by the source generator, and shipped to the
/// server as opaque IR — the server never compiles this source.
/// </summary>
[Plugin("guardian")]
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

    [LiveSetting]
    public string CalmStrength { get; set; } = "20";

    public bool ShouldHandle(MonsterAggroEvent e, HookContext ctx)
        => IsBullyingLowLevelPlayer(e.MonsterLevel, e.PlayerLevel, e.Distance, LevelGap, AggroRange, ProtectMaxLevel) &&
           // Gated host-world read: only calm a monster that is still alive. The call lowers to the
           // host.world.getHealth binding and requires game.world.monster.read.health, which the server
           // grants via game.world.monster.read.*.
           ctx.Host<IGameWorldAccess>().GetHealth(e.MonsterId) > 0;

    public void Handle(MonsterAggroEvent e, HookContext ctx)
        => ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId + ":" + CalmStrength);

    /// <summary>
    /// Reusable gate factored out with <c>[KernelMethod]</c>: the source generator inlines this body
    /// into <see cref="ShouldHandle"/> as if it were written there, so the shared "is this monster
    /// bullying a weaker player who is within aggro range?" rule can be named and unit-tested without
    /// leaving the sandbox. Live settings (<c>LevelGap</c> etc.) are passed in as arguments because an
    /// inlined kernel method is static and cannot read instance state directly.
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
