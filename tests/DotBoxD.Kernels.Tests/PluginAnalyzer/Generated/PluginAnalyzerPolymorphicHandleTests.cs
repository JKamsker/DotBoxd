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
    public void Result_hook_multiple_declaration_captures_in_one_filter_lower()
    {
        var source = Source("""
            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Attacker is PlayerCombatant attacker &&
                                      ctx.Victim is MonsterCombatant victim &&
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
        Assert.Contains("combatant.monster.is", generated);
        Assert.Contains("combatant.player.hasEquippedItem", generated);
    }

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

    [Fact]
    public void Explicit_this_live_setting_is_not_shadowed_by_polymorphic_capture()
    {
        const string source = """
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            [PolymorphicHandle(nameof(Id))]
            [HandleSubtype(typeof(PlayerCombatant), "player", "combatant.player", "combatant.player.read")]
            public abstract record Combatant(long Id);

            public sealed record PlayerCombatant(long Id) : Combatant(Id);

            public sealed record DamageEvent(Combatant Target);

            [Plugin("polymorphic-this-live-setting")]
            public sealed partial class DamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public bool Disabled { get; set; }

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => e.Target is PlayerCombatant Disabled && this.Disabled;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("damage", "hit");
            }
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("\"combatant.player.is\"", generated);
        Assert.Contains("Var(\"Disabled\")", generated);
    }

    [Fact]
    public void Explicit_this_hidden_non_live_member_is_not_lowered_as_live_setting()
    {
        const string source = """
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed record DamageEvent(int Damage);

            public abstract class DamageKernelBase
            {
                [LiveSetting]
                public bool Disabled { get; set; }
            }

            [Plugin("hidden-live-setting")]
            public sealed partial class DamageKernel : DamageKernelBase, IEventKernel<DamageEvent>
            {
                public new bool Disabled => true;

                public bool ShouldHandle(DamageEvent e, HookContext ctx)
                    => this.Disabled;

                public void Handle(DamageEvent e, HookContext ctx)
                    => ctx.Messages.Send("damage", "hit");
            }
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain("Var(\"Disabled\")", generated, StringComparison.Ordinal);
    }

    private static void AssertFailsSafe(string source)
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK113");
        var generated = result.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToArray();
        Assert.DoesNotContain(generated, source =>
            source.Contains("DamagePluginPackage", StringComparison.Ordinal)
            || source.Contains("DotBoxDHookChainInterceptors", StringComparison.Ordinal)
            || source.Contains("HookSubscriptionManifest", StringComparison.Ordinal));
    }

    private static string Source(
        string usage,
        bool includeMonsterSubtype = true,
        string keyMemberExpression = "nameof(Id)",
        string keyType = "long",
        string playerDiscriminator = "player",
        string playerBindingPrefix = "combatant.player",
        string playerCapability = "combatant.player.read",
        string playerHostBindingSuffix = "")
    {
        var monsterSubtype = includeMonsterSubtype
            ? """[HandleSubtype(typeof(MonsterCombatant), "monster", "combatant.monster", "combatant.monster.read")]"""
            : string.Empty;

        return $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [PolymorphicHandle({{keyMemberExpression}})]
            [HandleSubtype(typeof(PlayerCombatant), "{{playerDiscriminator}}", "{{playerBindingPrefix}}", "{{playerCapability}}")]
            {{monsterSubtype}}
            public abstract record Combatant({{keyType}} Id);

            public sealed record PlayerCombatant({{keyType}} Id) : Combatant(Id)
            {
                [HostBinding(
                    "combatant.player.hasEquippedItem",
                    "combatant.player.read",
                    SandboxEffect.Cpu | SandboxEffect.HostStateRead{{playerHostBindingSuffix}})]
                public bool HasEquippedItem(long itemRuntimeId) => throw new System.NotSupportedException();
            }

            public sealed record MonsterCombatant({{keyType}} Id) : Combatant(Id);

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(Combatant Attacker, Combatant Victim, int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            {{usage}}
            """;
    }
}
