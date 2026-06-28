using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class InvokeAsyncGeneratedReceiverSurpriseTests
{
    [Fact]
    public void Member_access_InvokeAsync_inside_generated_facade_respects_user_overload()
    {
        var result = RunGeneratorAndAssertCompiles(GeneratedFacadeBodySource("""
                public ValueTask<int> InvokeAsync(
                    Func<IGameWorldAccess, ValueTask<int>> lambda,
                    CancellationToken cancellationToken = default)
                    => new(7);

                public ValueTask<int> Probe()
                    => this.InvokeAsync(async (IGameWorldAccess world) =>
                    {
                        return world.GetHealth("monster-1");
                    });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.DoesNotContain("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InvokeAsync_on_subclass_of_generated_facade_reports_or_lowers()
    {
        var result = RunGenerator(UsageSource("""
            public sealed class DerivedServer : RemotePluginServer
            {
                public DerivedServer(DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService control)
                    : base(control)
                {
                }
            }

            public static ValueTask<int> Run(DerivedServer kernels)
                => kernels.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "DBXK100") ||
            source.Contains("AnonymousInvokeAsync", StringComparison.Ordinal),
            source);
    }

    [Fact]
    public void Same_compilation_generated_services_receiver_lowers_InvokeAsync()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
                => kernels.Services.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}
