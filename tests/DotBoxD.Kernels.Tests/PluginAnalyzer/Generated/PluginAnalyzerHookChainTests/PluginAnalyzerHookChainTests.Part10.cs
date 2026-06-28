using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_staged_alias_ref_reassignment_before_Run_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Plugins.Runtime.Hooks;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    Replace(ref staged, hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-2"));
                    staged.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }

                private static void Replace(
                    ref RemoteHookStage<DamageEvent, DamageEvent> stage,
                    RemoteHookStage<DamageEvent, DamageEvent> replacement)
                    => stage = replacement;
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_subscription_staged_alias_ref_reassignment_before_RunLocal_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Plugins.Runtime.Subscriptions;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions)
                {
                    var staged = subscriptions.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    Replace(ref staged, subscriptions.On<DamageEvent>().Where(e => e.TargetId == "monster-2"));
                    staged.RunLocal((e, ctx) => { });
                }

                private static void Replace(
                    ref RemoteSubscriptionStage<DamageEvent, DamageEvent> stage,
                    RemoteSubscriptionStage<DamageEvent, DamageEvent> replacement)
                    => stage = replacement;
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedLocalChain", StringComparison.Ordinal));
    }
}
