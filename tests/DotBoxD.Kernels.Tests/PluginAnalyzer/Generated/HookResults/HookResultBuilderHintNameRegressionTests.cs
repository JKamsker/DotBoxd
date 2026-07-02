using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultBuilderHintNameRegressionTests
{
    [Fact]
    public void HookResult_builders_use_collision_free_hint_names_for_distinct_namespaces()
    {
        var generated = PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            using DotBoxD.Abstractions;

            namespace A.B
            {
                [HookResult]
                public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            }

            namespace A_B
            {
                [HookResult]
                public readonly partial record struct DamageResult(bool Success, string? Reason, int Damage);
            }
            """);

        Assert.Equal(
            2,
            generated.Count(source => source.Contains("partial record struct DamageResult", StringComparison.Ordinal)));
    }

    [Fact]
    public void HookResult_builders_support_keyword_escaped_namespace_segments()
    {
        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources("""
            using DotBoxD.Abstractions;

            namespace Sample.@event;

            [HookResult]
            public readonly partial record struct KeywordNamespaceResult(
                bool Success,
                string? Reason,
                int Damage);
            """));

        Assert.Contains("namespace Sample.@event", generated, StringComparison.Ordinal);
        Assert.Contains(
            "partial record struct KeywordNamespaceResult : global::DotBoxD.Abstractions.IHookResult",
            generated,
            StringComparison.Ordinal);
        Assert.Contains("public KeywordNamespaceResult WithDamage(int damage)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void HookResult_hint_segments_treat_supplementary_scalars_as_single_units()
    {
        var deseretLetter = char.ConvertFromUtf32(0x10400);
        var emoji = char.ConvertFromUtf32(0x1F600);

        Assert.Equal(deseretLetter, InvokeHintNameSegment(deseretLetter));
        Assert.Equal("_x1F600", InvokeHintNameSegment(emoji));
    }

    private static string InvokeHintNameSegment(string segment)
    {
        var emitter = typeof(DotBoxD.Plugins.Analyzer.Analysis.PluginPackageGenerator)
            .Assembly
            .GetType(
                "DotBoxD.Plugins.Analyzer.Analysis.HookResults.HookResultBuilderEmitter",
                throwOnError: true)!;
        var method = emitter.GetMethod(
            "HintNameSegment",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return Assert.IsType<string>(method.Invoke(null, [segment]));
    }
}
