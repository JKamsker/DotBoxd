using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_manifest_plugin_id_that_does_not_match_module()
    {
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { PluginId = "other-plugin" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP011");
    }

    [Fact]
    public async Task Install_rejects_manifest_effects_that_do_not_match_verified_module()
    {
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Manifest = package.Manifest with { Effects = ["Cpu", "GameStateWrite", "Audit"] } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP041");
    }

    [Fact]
    public async Task Install_rejects_missing_kernel_entrypoint()
    {
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with { Entrypoints = package.Entrypoints with { Handle = "MissingHandle" } };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP032");
    }

    [Fact]
    public async Task Install_rejects_subscription_kernel_that_does_not_match_module_metadata()
    {
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [new HookSubscriptionManifest("DamageEvent", "OtherKernel")]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP013");
    }
}
