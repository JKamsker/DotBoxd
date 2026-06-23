using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerPolymorphicHandleTests
{
    [Fact]
    public void Result_hook_polymorphic_filter_lowers_discriminator_and_scoped_host_call()
    {
        var source = Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      (attacker).HasEquippedItem(9001L))
                        .Where(ctx => ctx.Victim is MonsterCombatant)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage * 2 }, 100);
            }
            """);
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.Contains("combatant.player.is", generated);
        Assert.Contains("combatant.player.hasEquippedItem", generated);
        Assert.Contains("\"combatant.player.hasEquippedItem\", [Var(\"e_Attacker\"), I64(9001L)]", generated);
        Assert.Contains("combatant.monster.is", generated);
        Assert.Contains("combatant.player.read", generated);
        Assert.Contains("combatant.monster.read", generated);
    }

    [Fact]
    public void Result_hook_polymorphic_filter_without_declared_subtype_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Victim is MonsterCombatant)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, includeMonsterSubtype: false));

    [Fact]
    public void Result_hook_declaration_pattern_inside_or_lowers_with_scoped_capture()
    {
        var source = Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => (ctx.Attacker is PlayerCombatant attacker &&
                                       attacker.HasEquippedItem(9001L)) ||
                                      ctx.Damage > 0)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.Contains("combatant.player.is", generated);
        Assert.Contains("combatant.player.hasEquippedItem", generated);
    }

    [Fact]
    public void Result_hook_declaration_pattern_inside_conditional_lowers_true_branch_capture()
    {
        var source = Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker
                            ? attacker.HasEquippedItem(9001L)
                            : false)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.Contains("combatant.player.is", generated);
        Assert.Contains("combatant.player.hasEquippedItem", generated);
    }

    [Fact]
    public void Result_hook_declaration_pattern_inside_result_expression_fails_safe()
        => AssertFailsSafe("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [PolymorphicHandle(nameof(Id))]
            [HandleSubtype(typeof(PlayerCombatant), "player", "combatant.player", "combatant.player.read")]
            public abstract record Combatant(long Id);

            public sealed record PlayerCombatant(long Id) : Combatant(Id)
            {
                [HostBinding(
                    "combatant.player.hasEquippedItem",
                    "combatant.player.read",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                public bool HasEquippedItem(long itemRuntimeId) => throw new System.NotSupportedException();
            }

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(Combatant Attacker);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, bool IsEquipped);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithIsEquipped(
                            ctx.Attacker is PlayerCombatant attacker &&
                            attacker.HasEquippedItem(9001L)), 0);
            }
            """);

    [Fact]
    public void Result_hook_recursive_key_property_pattern_lowers()
    {
        var source = Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant { Id: > 100L } attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.Contains("combatant.player.is", generated);
        Assert.Contains("combatant.player.hasEquippedItem", generated);
        Assert.Contains("I64(100L)", generated);
    }

    [Fact]
    public void Result_hook_recursive_capture_inside_not_fails_safe()
        => AssertFailsSafe(Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => !(ctx.Attacker is PlayerCombatant { Id: > 100L } attacker))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """));

    [Fact]
    public void Result_hook_async_scoped_host_call_adds_runtime_async_requirements()
    {
        var source = Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      attacker.HasEquippedItem(9001L))
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """, playerHostBindingSuffix: ", IsAsync = true");
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        Assert.Contains("dotboxd.runtime.async", generated);
        Assert.Contains("Concurrency", generated);
    }
}
