using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_rpc_package_that_also_declares_event_subscriptions()
    {
        using var server = PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var package = RpcKernelTestPackages.MonsterKiller();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [new HookSubscriptionManifest("DamageEvent", "KillMonsters")]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallServerExtensionAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK073");
    }
}
