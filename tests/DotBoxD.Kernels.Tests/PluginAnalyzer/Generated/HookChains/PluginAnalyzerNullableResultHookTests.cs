using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginAnalyzerNullableResultHookTests
{
    [Fact]
    public void Lowers_a_Register_result_chain_with_nullable_scalar_fields()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int? Damage, bool? CanDie);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithDamage(ctx.Damage).WithCanDie(null), 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
        Assert.Contains(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("UseGeneratedResultChain", StringComparison.Ordinal));
    }

    [Fact]
    public void Lowers_nullable_scalar_literal_fields_in_object_initializers()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int? Damage, bool? CanDie);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => new DamageResult { Success = true, Damage = 1, CanDie = false }, 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Lowers_nullable_float_literal_fields_in_fluent_builders()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(float Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, float? Amount);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => DamageResult.Ok().WithAmount(1f), 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
    }

    [Fact]
    public void Lowers_nullable_float_literal_fields_in_object_initializers()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("combat.damage", typeof(DamageResult))]
            public sealed record DamageCtx(float Damage);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, float? Amount);

            public static class Usage
            {
                public static void Configure(HookRegistry hooks)
                    => hooks.On<DamageCtx>()
                        .Register(ctx => new DamageResult { Success = true, Amount = 1f }, 100);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "DBXK113");
    }
}
