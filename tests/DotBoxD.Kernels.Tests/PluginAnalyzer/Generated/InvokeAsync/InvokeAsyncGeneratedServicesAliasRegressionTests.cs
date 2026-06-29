using static DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.InvokeAsyncGenerationTestSources;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed partial class InvokeAsyncGeneratedReceiverSurpriseTests
{
    [Fact]
    public void Generated_services_receiver_alias_local_lowers_InvokeAsync()
    {
        var result = RunGeneratorAndAssertCompiles(UsageSource("""
            public static ValueTask<int> Run(RemotePluginServer kernels)
            {
                var services = kernels.Services;
                return services.InvokeAsync(async (IGameWorldAccess world) =>
                {
                    return world.GetHealth("monster-1");
                });
            }
            """));
        var source = string.Join("\n", result.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "DBXK100");
        Assert.Contains("AnonymousInvokeAsync", source, StringComparison.Ordinal);
    }
}
