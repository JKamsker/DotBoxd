using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class KernelRegistryLookupConsistencyTests
{
    [Fact]
    public async Task Get_resolves_the_same_staged_kernel_ids_as_TryGet()
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        using var session = server.CreateSession();
        var staged = await session.InstallStagedAsync(FireDamagePluginPackage.Create());

        Assert.True(server.Kernels.TryGet(staged.Manifest.PluginId, out var byPluginId));
        Assert.Same(staged, byPluginId);
        Assert.True(server.Kernels.TryGet(staged.InstallId, out var byInstallId));
        Assert.Same(staged, byInstallId);

        Assert.Same(staged, server.Kernels.Get(staged.Manifest.PluginId));
        Assert.Same(staged, server.Kernels.Get(staged.InstallId));
    }
}
