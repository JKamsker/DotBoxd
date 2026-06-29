using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.HookResults;

public sealed class HookResultBuilderKeywordFieldTests
{
    [Fact]
    public void HookResult_builder_escapes_keyword_field_names_in_with_initializer()
    {
        const string source = """
            using DotBoxD.Abstractions;

            namespace Sample;

            [HookResult]
            public readonly partial record struct KeywordResult(
                bool Success,
                string? Reason,
                int @class);
            """;

        var generated = string.Join("\n", PluginAnalyzerGeneratedPackageFactory.GeneratedSources(source));

        Assert.Contains("public KeywordResult Withclass(int @class)", generated, StringComparison.Ordinal);
        Assert.Contains("this with { @class = @class }", generated, StringComparison.Ordinal);
    }
}
