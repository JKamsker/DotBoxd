using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_staged_Where_then_UseGeneratedChain_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks, PluginPackage package)
                    => hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")
                        .UseGeneratedChain(package);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UseGeneratedChain", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_subscription_staged_Where_then_UseGeneratedChain_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions, PluginPackage package)
                    => subscriptions.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1")
                        .UseGeneratedChain(package);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UseGeneratedChain", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_Select_then_UseGeneratedLocalChain_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks, PluginPackage package)
                    => hooks.On<DamageEvent>()
                        .Select(e => e.TargetId)
                        .UseGeneratedLocalChain(package, id => { });
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UseGeneratedLocalChain", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_subscription_staged_Select_then_UseGeneratedLocalChain_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteSubscriptionRegistry subscriptions, PluginPackage package)
                    => subscriptions.On<DamageEvent>()
                        .Select(e => e.TargetId)
                        .UseGeneratedLocalChain(package, id => { });
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UseGeneratedLocalChain", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_conditional_RunLocal_reports_DBXK100()
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
                    RemoteHookStage<DamageEvent, string>? staged = hooks.On<DamageEvent>()
                        .Select(e => e.TargetId);

                    staged?.RunLocal((id, ctx) => { });
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("RunLocal", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("conditional access", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_conditional_Run_reports_DBXK100()
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
                    RemoteHookStage<DamageEvent, string>? staged = hooks.On<DamageEvent>()
                        .Select(e => e.TargetId);

                    staged?.Run((id, ctx) => ctx.Messages.Send(id, "hit"));
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Run", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("conditional access", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_conditional_UseGeneratedChain_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Plugins.Runtime.Hooks;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks, PluginPackage package)
                {
                    RemoteHookStage<DamageEvent, string>? staged = hooks.On<DamageEvent>()
                        .Select(e => e.TargetId);

                    staged?.UseGeneratedChain(package);
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UseGeneratedChain", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("conditional access", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_conditional_UseGeneratedLocalChain_reports_DBXK100()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Plugins.Runtime.Hooks;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks, PluginPackage package)
                {
                    RemoteHookStage<DamageEvent, string>? staged = hooks.On<DamageEvent>()
                        .Select(e => e.TargetId);

                    staged?.UseGeneratedLocalChain(package, id => { });
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("UseGeneratedLocalChain", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("conditional access", diagnostic.GetMessage(), StringComparison.Ordinal);
    }
}
