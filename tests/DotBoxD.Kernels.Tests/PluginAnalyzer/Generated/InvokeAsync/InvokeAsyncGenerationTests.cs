using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class InvokeAsyncGenerationTests
{
    [Fact]
    public void Block_body_no_capture_lambda_generates_anonymous_package()
    {
        var result = RunGenerator(NoCaptureSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("$anon:", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getHealth", source, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.health", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Object_snapshot_member_access_generates_record_get_package()
    {
        var result = RunGenerator(ObjectSurfaceSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("host.world.getMonster", source, StringComparison.Ordinal);
        Assert.Contains("game.world.monster.read.snapshot", source, StringComparison.Ordinal);
        Assert.Contains("record.get", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Expression_body_lambda_is_ignored()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.InvokeAsync((IGameWorldAccess world) => new ValueTask<int>(world.GetHealth("monster-1")));
            """));

        Assert.DoesNotContain(
            result.GeneratedTrees,
            tree => tree.ToString().Contains("InvokeAsync_", StringComparison.Ordinal));
    }

    [Fact]
    public void Implicit_capture_generates_reflection_arguments_and_sync_out()
    {
        var result = RunGenerator(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
            {
                var monsterId = "monster-1";
                var lastHealth = 0;
                return kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    lastHealth = world.GetHealth(monsterId);
                    return lastHealth;
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("__ReadCapture<", source, StringComparison.Ordinal);
        Assert.Contains("__WriteCapture(lambda, \"lastHealth\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"monsterId\\\"", source, StringComparison.Ordinal);
        Assert.Contains("\\\"name\\\":\\\"lastHealth\\\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_capture_bag_generates_sync_in_and_sync_out_package()
    {
        var result = RunGenerator(CaptureBagSource);
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("InvokeAsync_", source, StringComparison.Ordinal);
        Assert.Contains("\"parameters\\\":[{\\\"name\\\":\\\"bag\\\"", source, StringComparison.Ordinal);
        Assert.Contains("__syncOut_LastHealth", source, StringComparison.Ordinal);
        Assert.Contains("captures.LastHealth =", source, StringComparison.Ordinal);
    }
}
