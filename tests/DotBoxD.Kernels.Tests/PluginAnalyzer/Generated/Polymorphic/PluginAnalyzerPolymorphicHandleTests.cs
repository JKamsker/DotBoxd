using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerPolymorphicHandleTests
{
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
