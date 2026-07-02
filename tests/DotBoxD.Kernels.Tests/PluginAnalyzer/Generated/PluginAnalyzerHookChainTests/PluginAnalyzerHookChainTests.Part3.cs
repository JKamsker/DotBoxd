using Microsoft.CodeAnalysis;
namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Lowers_a_RegisterLocal_block_body_with_local_logic_returning_a_result_builder_chain()
    {
        var result = RunGenerator("""
            using System;
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
                        .RegisterLocal((ctx, hookContext) =>
                        {
                            var capped = Math.Min(ctx.Damage, 10);
                            return DamageResult.Ok().WithDamage(capped);
                        }, 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedLocalResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_Register_builder_constant_to_the_result_field_type()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, long Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(1), 100);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("I64(1L)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_builder_non_constant_numeric_mismatch_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, long Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage), 100);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Register_with_author_defined_result_builder_member_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public static DamageResult Ok() => new() { Success = true, Damage = 999 };
            }

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage), 100);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Register_with_author_defined_with_member_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public DamageResult WithDamage(int damage) => this with { Damage = 999 };
            }

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage), 100);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Register_with_author_defined_non_colliding_with_overload_still_lowers()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage)
            {
                public DamageResult WithDamage() => this with { Damage = 999 };
            }

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage), 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void RegisterLocal_returning_the_wrong_result_type_reports_DBXK113()
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

            [HookResult]
            public readonly partial record struct OtherResult(bool Success, string? Reason);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .RegisterLocal((ctx, hookContext) => new OtherResult { Success = true }, 0);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Register_on_a_context_without_Hook_reports_DBXK113()
    {
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NoHookCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<NoHookCtx>()
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Unlowered_Register_reports_DBXK113_as_a_warning()
    {
        // A sandbox Register that fails to lower has no in-process fallback (it always throws at first dispatch),
        // so it is raised to Warning so the author sees it rather than the default-suppressed Info.
        var result = RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record NoHookCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<NoHookCtx>()
                        .Register(ctx => new DamageResult { Success = true, Damage = ctx.Damage }, 0);
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXK113"));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

}
