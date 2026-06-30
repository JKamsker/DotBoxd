using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Custom_Where_returning_remote_pipeline_does_not_lower_as_hook_stage()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public sealed class CustomHooks
            {
                public CustomPipeline<T> On<T>() => default!;
            }

            public sealed class CustomPipeline<T>
            {
                public RemoteHookPipeline<T> Where(Func<T, bool> filter) => default!;
            }

            public static class Usage
            {
                public static void Configure(CustomHooks hooks)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK114"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_Select_returning_remote_stage_does_not_lower_as_hook_stage()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Plugins.Runtime.Hooks;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public sealed class CustomHooks
            {
                public CustomPipeline<T> On<T>() => default!;
            }

            public sealed class CustomPipeline<T>
            {
                public RemoteHookStage<T, string> Select(Func<T, string> projection) => default!;
            }

            public static class Usage
            {
                public static void Configure(CustomHooks hooks)
                    => hooks.On<DamageEvent>()
                        .Select(e => e.TargetId)
                        .Run((id, ctx) => ctx.Messages.Send(id, "hit"));
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK114"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }
}
