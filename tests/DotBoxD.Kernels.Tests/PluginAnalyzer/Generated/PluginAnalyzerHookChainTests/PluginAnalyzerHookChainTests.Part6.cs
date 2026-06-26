using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Remote_assigned_staged_hook_reports_DBXK100_error()
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
                    var pipeline = hooks.On<DamageEvent>();
                    pipeline = pipeline.Where(e => e.TargetId == "monster-1");
                    pipeline.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("assigned", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_Use_through_two_locals_reports_DBXK100_error()
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
                    var alias = staged;
                    alias.Use<DamageKernel>();
                }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK100"));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Where/Select", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_staged_Select_from_alias_lowers()
    {
        const string source = """
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    var staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-1");
                    staged.Select(e => e.TargetId)
                        .Run((targetId, ctx) => ctx.Messages.Send(targetId, "hit"));
                }
            }
            """;
        var result = RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.Contains(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Remote_reassigned_staged_Run_alias_reports_DBXK114_instead_of_stale_lowering()
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
                    staged = hooks.On<DamageEvent>()
                        .Where(e => e.TargetId == "monster-2");
                    staged.Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK114");
        Assert.DoesNotContain(result.GeneratedTrees, tree => tree.ToString().Contains("UseGeneratedChain", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelMethod_reordered_nonrepeatable_arguments_report_DBXK114()
    {
        var result = RunGenerator("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [DotBoxDService]
            public interface IProbe
            {
                [HostBinding("probe.next", "probe.next", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Next(string id);
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Where((e, ctx) => IsOrdered(
                            right: ctx.Host<IProbe>().Next("B"),
                            left: ctx.Host<IProbe>().Next("A")))
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));

                [KernelMethod]
                public static bool IsOrdered(int left, int right) => left < right;
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("call-site evaluation order", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelMethod_send_helper_reordered_nonrepeatable_arguments_report_DBXK114()
    {
        var result = RunGenerator("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            [DotBoxDService]
            public interface IProbe
            {
                [HostBinding("probe.next", "probe.next", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                string Next(string id);
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent>()
                        .Run((e, ctx) => SendPair(
                            ctx,
                            message: ctx.Host<IProbe>().Next("B"),
                            target: ctx.Host<IProbe>().Next("A")));

                [KernelMethod]
                public static void SendPair(HookContext ctx, string target, string message)
                    => ctx.Messages.Send(target, message);
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("call-site evaluation order", StringComparison.Ordinal));
    }
}
