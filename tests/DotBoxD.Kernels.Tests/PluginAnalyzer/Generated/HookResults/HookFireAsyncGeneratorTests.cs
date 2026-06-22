using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookFireAsyncGeneratorTests
{
    [Fact]
    public void FireAsync_extensions_are_internal_so_internal_hook_types_compile()
    {
        _ = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            [Hook("internal.damage", typeof(DamageResult))]
            internal sealed record DamageCtx(int Damage);

            [HookResult]
            internal readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);

            internal static class Usage
            {
                public static async ValueTask<int> FireAsync(HookRegistry hooks)
                {
                    var result = await hooks.FireAsync(new DamageCtx(5));
                    return result?.Damage ?? 0;
                }
            }
            """);
    }

    [Fact]
    public void Invalid_hook_result_does_not_emit_fire_async_extension_errors()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("bad.damage", typeof(BadResult))]
            public sealed record DamageCtx(int Damage);

            [HookResult]
            public readonly partial record struct BadResult(bool Success, int Damage);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DBXK112");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0315");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }

    [Fact]
    public void Generic_hook_context_does_not_emit_unbound_type_parameter_extension()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [Hook("generic.damage", typeof(DamageResult))]
            public sealed record GenericDamageCtx<T>(T Payload);

            [HookResult]
            public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            """;

        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator(source);
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS0246");
        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.GetText().ToString().Contains("HookRegistryFireAsyncExtensions", StringComparison.Ordinal));
    }
}
