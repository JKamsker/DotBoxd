using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginLiveUpdateRecoveryTests
{
    [Fact]
    public async Task AsyncSet_flush_recovers_after_later_successful_update()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 10_001;
        await Assert.ThrowsAnyAsync<Exception>(async () => await kernel.FlushUpdatesAsync().AsTask());

        kernel.Value.MinDamage = 250;
        await kernel.FlushUpdatesAsync();

        Assert.Null(kernel.LastAsyncUpdateError);
        Assert.Equal(250, kernel.Kernel.Value.Get<int>("MinDamage"));
    }
}
