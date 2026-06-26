using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncSimpleLambdaGenerationTests
{
    [Fact]
    public void Unparenthesized_no_capture_lambda_generates_anonymous_package()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async world =>
                {
                    var hp = world.GetHealth("monster-1");
                    return hp;
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getHealth", source, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.health", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Unparenthesized_no_capture_lambda_lowers_identically_to_parenthesized()
    {
        var simple = StripInterceptsLocation(RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async world =>
                {
                    var hp = world.GetHealth("monster-1");
                    return hp;
                });
            """)));
        var parenthesized = StripInterceptsLocation(RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    var hp = world.GetHealth("monster-1");
                    return hp;
                });
            """)));

        Assert.Equal(parenthesized, simple);
    }

    [Fact]
    public void Unparenthesized_implicit_capture_lambda_generates_reflection_captures()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
            {
                var monsterId = "monster-1";
                var lastHealth = 0;
                return kernels.InvokeAsync(async world =>
                {
                    lastHealth = world.GetHealth(monsterId);
                    return lastHealth;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("__ReadCapture<", source, StringComparison.Ordinal);
        Assert.Contains("__WriteCapture(lambda, \"lastHealth\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"monsterId\\\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"lastHealth\\\"", source, StringComparison.Ordinal);
    }

    private static string StripInterceptsLocation(GeneratorDriverRunResult result)
    {
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));
        var kept = source
            .Split('\n')
            .Where(line => !line.Contains("InterceptsLocation", StringComparison.Ordinal));
        return string.Join("\n", kept);
    }
}
