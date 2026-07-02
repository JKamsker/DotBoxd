using Microsoft.CodeAnalysis;
namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class PluginAnalyzerHookChainTests
{
    [Fact]
    public void Lowers_a_remote_RegisterLocal_result_chain_to_a_remote_local_result_install()
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
                        .RegisterLocal((ctx, hookContext) => new DamageResult { Success = true, Damage = ctx.Damage }, 25);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedLocalResultChain<global::Sample.DamageResult>", generated, StringComparison.Ordinal);
        Assert.Contains("ResultType = \"global::Sample.DamageResult\"", generated, StringComparison.Ordinal);
        Assert.Contains("ResultLocalTerminal = true", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("                LocalTerminal = true", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Remote_RegisterLocal_result_chain_after_Select_reports_DBXK113()
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
                        .Select(ctx => ctx.Damage)
                        .RegisterLocal((damage, hookContext) => DamageResult.Ok().WithDamage(damage), 25);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Generated_remote_Register_result_chain_after_Where_uses_pipeline_receiver()
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
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage), 25);
            }
            """);

        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("RemoteHookPipeline<global::Sample.DamageCtx>", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteHookStage<global::Sample.DamageCtx", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Lowers_a_Register_fluent_builder_chain_to_a_result_install()
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
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage * 2), 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_Register_result_chain_with_named_reordered_arguments()
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
                        .Register(priority: 100, handler: ctx => DamageResult.Ok().WithDamage(ctx.Damage));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_a_RegisterLocal_result_chain_with_named_reordered_arguments()
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
                        .RegisterLocal(
                            priority: 100,
                            handler: (ctx, hookContext) => DamageResult.Ok().WithDamage(ctx.Damage));
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedLocalResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void RegisterLocal_sync_handler_named_ct_uses_context_shape()
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
                        .RegisterLocal((ctx, ct) => DamageResult.Ok().WithDamage(ctx.Damage), 100);
            }
            """);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedLocalResultChain", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "global::System.Func<global::Sample.DamageCtx, global::System.Threading.CancellationToken",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterLocal_async_handler_named_ct_uses_context_shape()
    {
        var result = RunGenerator("""
            using System.Threading.Tasks;
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
                        .RegisterLocal(async (ctx, ct) =>
                        {
                            await Task.Yield();
                            return DamageResult.Ok().WithDamage(ctx.Damage);
                        }, 100);
            }
            """);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedLocalResultChain", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "global::System.Func<global::Sample.DamageCtx, global::System.Threading.CancellationToken",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterLocal_async_handler_typed_ct_uses_cancellation_token_shape()
    {
        var result = RunGenerator("""
            using System.Threading;
            using System.Threading.Tasks;
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
                        .RegisterLocal(async (DamageCtx ctx, CancellationToken ct) =>
                        {
                            await Task.Yield();
                            return DamageResult.Ok().WithDamage(ctx.Damage);
                        }, 100);
            }
            """);
        var generated = string.Join(
            Environment.NewLine,
            result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains("UseGeneratedLocalResultChain", generated, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Func<global::Sample.DamageCtx, global::System.Threading.CancellationToken",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Lowers_a_RegisterLocal_block_body_returning_a_result_builder_chain()
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
                        .RegisterLocal((ctx, hookContext) =>
                        {
                            return DamageResult.Ok().WithDamage(ctx.Damage);
                        }, 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedLocalResultChain", StringComparison.Ordinal));
    }

}
