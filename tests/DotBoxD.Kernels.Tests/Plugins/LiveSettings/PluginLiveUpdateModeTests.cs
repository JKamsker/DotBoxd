using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

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

    [Fact]
    public async Task Revoked_typed_kernel_rejects_update_mode_mutation()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        Assert.True(server.Uninstall("fire-damage"));

        var exception = Assert.Throws<SandboxRuntimeException>(
            () => kernel.UpdateMode = LiveUpdateMode.AsyncSet);

        Assert.Equal(SandboxErrorCode.PolicyDenied, exception.Error.Code);
        Assert.Equal(LiveUpdateMode.Sync, kernel.UpdateMode);
    }
}
