using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncSimpleLambdaGenerationTests
{
    // An unparenthesized single-parameter lambda is always implicitly typed. The world
    // parameter type cannot be resolved while the generated plugin server facade (which would
    // give the lambda its delegate target type) is still being produced, so the body cannot be
    // lowered. The generator must surface a clear, actionable DBXK100 that names the explicit
    // form, instead of the generic "supported block body and capture shape" message.

    [Fact]
    public void Unparenthesized_implicitly_typed_lambda_reports_actionable_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async world =>
                {
                    var hp = world.GetHealth("monster-1");
                    return hp;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("explicitly typed world parameter", StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains("IGameWorldAccess world", StringComparison.Ordinal));
    }

    [Fact]
    public void Parenthesized_implicitly_typed_lambda_reports_actionable_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (world) =>
                {
                    var hp = world.GetHealth("monster-1");
                    return hp;
                });
            """));

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("explicitly typed world parameter", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicitly_typed_lambda_still_generates_without_diagnostic()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var hp = world.GetHealth("monster-1");
                    return hp;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getHealth", source, StringComparison.Ordinal);
    }
}
