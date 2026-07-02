using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_staged_Where_returned_from_helper_then_Use_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static RemoteHookPipeline<DamageEvent> Filter(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1");

                public static void Configure(RemoteHookRegistry hooks)
                    => Filter(hooks).Use<DamageKernel>();
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_Where_returned_from_helper_with_nested_return_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static RemoteHookPipeline<DamageEvent> Filter(RemoteHookRegistry hooks)
                {
                    RemoteHookPipeline<DamageEvent> Unfiltered()
                        => hooks.On<DamageEvent>();

                    _ = Unfiltered;
                    return hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1");
                }

                public static void Configure(RemoteHookRegistry hooks)
                    => Filter(hooks).Use<DamageKernel>();
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_conditional_staged_Where_then_Use_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks, bool filter)
                {
                    var pipeline = filter
                        ? hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1")
                        : hooks.On<DamageEvent>();
                    pipeline.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Use", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_local_reassigned_before_Use_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged = hooks.On<DamageEvent>();
                    staged.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_discarded_null_forgiving_staged_hook_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var pipeline = hooks.On<DamageEvent>();
                    pipeline.Where(e => e.TargetId == "monster-1")!;
                    pipeline.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_discarded_assignment_staged_hook_reports_DBXK100_error()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var pipeline = hooks.On<DamageEvent>();
                    _ = pipeline.Where(e => e.TargetId == "monster-1");
                    pipeline.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_discarded_stage_inside_nested_lambda_does_not_block_outer_Run()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var pipeline = hooks.On<DamageEvent>();
                    Action delayed = () => pipeline.Where(e => e.TargetId == "monster-1");
                    _ = delayed;
                    pipeline.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK100" or "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_hook_Where_on_receiver_expression_does_not_block_Run_lowering()
    {
        var result = RunGenerator("""
            using System.Linq;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var pipeline = hooks.On<DamageEvent>();
                    pipeline.ToString().Where(c => c == 'x');
                    pipeline.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK100" or "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_staged_alias_reassigned_after_Run_still_lowers_terminal()
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
                    staged.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                    staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-2");
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "DBXK100" or "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_staged_Where_returned_from_local_function_then_Use_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);
            public sealed class DamageKernel;

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    RemoteHookPipeline<DamageEvent> Filter()
                        => hooks.On<DamageEvent>().Where(e => e.TargetId == "monster-1");

                    Filter().Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
