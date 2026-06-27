using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncCaptureAliasRegressionTests
{
    [Fact]
    public void Explicit_capture_bag_alias_sync_out_initializer_reads_capture_parameter_not_alias()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public sealed class Capture
            {
                public string MonsterId { get; set; } = "";
                public int LastHealth { get; set; }
            }

            public static ValueTask<int> Run(RemotePluginServer kernels, Capture captures)
                => kernels.InvokeAsync(captures, async (IGameWorldAccess world, Capture bag) =>
                {
                    var alias = bag;
                    alias.LastHealth = world.GetHealth(alias.MonsterId);
                    return alias.LastHealth;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("\\\"var\\\":\\\"alias\\\"},{\\\"i32\\\":1", source, StringComparison.Ordinal);
        Assert.Contains("\\\"var\\\":\\\"bag\\\"},{\\\"i32\\\":1", source, StringComparison.Ordinal);
    }
}
