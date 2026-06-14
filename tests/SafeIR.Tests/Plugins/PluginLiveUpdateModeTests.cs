using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginLiveUpdateModeTests
{
    [Fact]
    public async Task Kernel_rejects_unsupported_live_update_mode()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        Assert.Throws<ArgumentOutOfRangeException>(() => kernel.UpdateMode = (LiveUpdateMode)123);
        Assert.Equal(LiveUpdateMode.Sync, kernel.UpdateMode);
    }
}
