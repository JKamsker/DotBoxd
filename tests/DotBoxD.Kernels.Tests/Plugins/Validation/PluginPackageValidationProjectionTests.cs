using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_projected_type_without_local_terminal()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions = [subscription with { ProjectedType = "string" }]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK031" &&
            d.Message.Contains("projectedType", StringComparison.Ordinal) &&
            d.Message.Contains("localTerminal", StringComparison.Ordinal));
    }
}
