using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginResultPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_result_local_terminal_without_result_type()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [subscription with { ResultLocalTerminal = true }]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK031");
    }

    [Fact]
    public async Task Install_rejects_result_metadata_combined_with_runlocal_projection_metadata()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        LocalTerminal = true,
                        ProjectedType = "string",
                        ResultType = "global::Sample.DamageResult"
                    }
                ]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK031");
    }
}
