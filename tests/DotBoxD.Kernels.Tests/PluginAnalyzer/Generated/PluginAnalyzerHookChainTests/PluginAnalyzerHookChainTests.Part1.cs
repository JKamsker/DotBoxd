using Microsoft.CodeAnalysis;
namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Does_not_lower_an_element_only_Run_terminal()
    {
        // An element-only terminal has no context, so it cannot ctx.Messages.Send — the only lowerable
        // terminal effect. It must fail safe: no HookChain_ package, leaving the runtime terminal to throw.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MonsterAggroEvent(string MonsterId, int Distance);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<MonsterAggroEvent>()
                        .Where((e, ctx) => e.Distance <= 5)
                        .Run(e => default);
            }
            """);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_lower_hook_chain_with_unsupported_event_property_type()
    {
        var result = RunGenerator("""
            using System;
            using System.Threading;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MixedEvent(string TargetId, CancellationToken Cancel);

            public static class Usage
            {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<MixedEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("HookChain_", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_hook_chain_with_temporal_event_property_types()
    {
        var result = RunGenerator("""
            using System;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record MixedEvent(string TargetId, DateTime When, DateOnly Day, TimeOnly Time, TimeSpan Delay);

            public static class Usage
            {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<MixedEvent>()
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.Contains("HookChain_", generated, StringComparison.Ordinal);
        Assert.Contains("SandboxType.Record(new global::DotBoxD.Kernels.Sandbox.SandboxType[] { global::DotBoxD.Kernels.Sandbox.SandboxType.I64, global::DotBoxD.Kernels.Sandbox.SandboxType.I64 })", generated, StringComparison.Ordinal);
        Assert.Contains("new global::DotBoxD.Kernels.Parameter(\"e_Day\", global::DotBoxD.Kernels.Sandbox.SandboxType.I32)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::DotBoxD.Kernels.Parameter(\"e_Time\", global::DotBoxD.Kernels.Sandbox.SandboxType.I64)", generated, StringComparison.Ordinal);
        Assert.Contains("new global::DotBoxD.Kernels.Parameter(\"e_Delay\", global::DotBoxD.Kernels.Sandbox.SandboxType.I64)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Interceptor_attribute_coexists_with_existing_definition()
    {
        var output = RunGeneratorCompilation("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace System.Runtime.CompilerServices
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                file sealed class InterceptsLocationAttribute : global::System.Attribute
                {
                    public InterceptsLocationAttribute(int version, string data) { }
                }
            }

            namespace Sample
            {

                public sealed record ExistingAttributeEvent(string TargetId);

                public static class Usage
                {
                    public static void Configure(HookRegistry hooks)
                        => hooks.On<ExistingAttributeEvent>()
                            .Run((e, ctx) => ctx.Messages.Send(e.TargetId, "hit"));
                }
            }
            """);

        Assert.DoesNotContain(
            output.GetDiagnostics(),
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Id is "CS0101" or "CS0111");
    }

    [Fact]
    public void Lowers_a_Register_result_chain_to_a_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public enum Relation { Pve = 0, Pvp = 1 }

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(Relation Relation, int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Relation == Relation.Pve)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage * 2 }, 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Result_hook_manifest_uses_the_declared_hook_name()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("\"combat.damage\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Sample.DamageCtx\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_hook_chain_manifest_uses_the_declared_hook_name()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(string TargetId);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Run((ctx, hookContext) => hookContext.Messages.Send(ctx.TargetId, "hit"));
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK100");
        Assert.Contains("\"combat.damage\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Sample.DamageCtx\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Lowers_a_RegisterLocal_result_chain_to_a_local_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.death", typeof(DeathResult))]
            public sealed record DeathCtx(int FatalDamage);

            [HookResult]
            public readonly partial record struct DeathResult(bool Success, string? Reason, int Mitigated);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DeathCtx>()
                        .Where(ctx => ctx.FatalDamage > 0)
                        .RegisterLocal((ctx, hookContext) => new DeathResult { Success = true }, 5);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedLocalResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_remote_Register_result_chain_to_a_remote_result_install()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Where(ctx => ctx.Damage > 0)
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 25);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedResultChain<global::Sample.DamageResult>", generated, StringComparison.Ordinal);
        Assert.Contains("ResultType = \"global::Sample.DamageResult\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("                LocalTerminal = true", generated, StringComparison.Ordinal);
    }

}
