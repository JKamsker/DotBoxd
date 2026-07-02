using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class InstalledKernelAtomicCancellationTests
{
    [Fact]
    public async Task Atomic_typed_modify_observes_pre_canceled_token_before_mutating_settings()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await kernel.ModifyAsync(
                state => state.MinDamage = 250,
                atomic: true,
                cancellationToken: cts.Token).AsTask());

        Assert.Equal(100, kernel.Value.MinDamage);
        Assert.Equal(100, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task Atomic_direct_settings_modify_observes_pre_canceled_token_before_mutating_settings()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await installed.ModifySettingsAsync(
                new Dictionary<string, object?>
                {
                    ["MinDamage"] = 250
                },
                atomic: true,
                cancellationToken: cts.Token).AsTask());

        Assert.Equal(100, kernel.Value.MinDamage);
        Assert.Equal(100, installed.Value.Get<int>("MinDamage"));
    }
}
