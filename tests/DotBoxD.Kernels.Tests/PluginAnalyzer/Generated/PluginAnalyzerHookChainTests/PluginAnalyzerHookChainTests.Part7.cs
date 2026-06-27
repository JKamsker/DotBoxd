namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_staged_Run_with_null_forgiving_stage_lowers()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")!
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_staged_Run_with_null_forgiving_alias_lowers()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged!.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }
}
