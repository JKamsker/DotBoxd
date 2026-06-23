using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerPolymorphicHandleTests
{
    [Fact]
    public void Result_hook_multiple_declaration_captures_can_use_each_scoped_receiver()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(SourceWithMonsterBinding("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      ctx.Victim is MonsterCombatant victim &&
                                      attacker.HasEquippedItem(9001L) &&
                                      victim.IsBoss())
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """));
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.Contains("\"combatant.player.hasEquippedItem\", [Var(\"e_Attacker\"), I64(9001L)]", generated);
        Assert.Contains("\"combatant.monster.isBoss\", [Var(\"e_Victim\")]", generated);
    }

    [Fact]
    public void Result_hook_positional_recursive_pattern_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant(> 100L) attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """));

    [Fact]
    public void Result_hook_recursive_pattern_hidden_key_member_fails_safe()
        => AssertFailsSafe("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [PolymorphicHandle(nameof(Id))]
            [HandleSubtype(typeof(PlayerCombatant), "player", "combatant.player", "combatant.player.read")]
            public abstract record Combatant(long Id);

            public sealed record PlayerCombatant(long BaseId) : Combatant(BaseId)
            {
                public new long Id => BaseId + 1;

                [HostBinding(
                    "combatant.player.hasEquippedItem",
                    "combatant.player.read",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                public bool HasEquippedItem(long itemRuntimeId) => throw new System.NotSupportedException();
            }

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(Combatant Attacker, int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant { Id: > 100L } attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

    private static string SourceWithMonsterBinding(string usage)
        => $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [PolymorphicHandle(nameof(Id))]
            [HandleSubtype(typeof(PlayerCombatant), "player", "combatant.player", "combatant.player.read")]
            [HandleSubtype(typeof(MonsterCombatant), "monster", "combatant.monster", "combatant.monster.read")]
            public abstract record Combatant(long Id);

            public sealed record PlayerCombatant(long Id) : Combatant(Id)
            {
                [HostBinding(
                    "combatant.player.hasEquippedItem",
                    "combatant.player.read",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                public bool HasEquippedItem(long itemRuntimeId) => throw new System.NotSupportedException();
            }

            public sealed record MonsterCombatant(long Id) : Combatant(Id)
            {
                [HostBinding(
                    "combatant.monster.isBoss",
                    "combatant.monster.read",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                public bool IsBoss() => throw new System.NotSupportedException();
            }

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(Combatant Attacker, Combatant Victim, int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            {{usage}}
            """;
}
