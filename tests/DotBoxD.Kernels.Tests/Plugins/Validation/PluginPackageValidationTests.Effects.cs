using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_duplicate_manifest_effects()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                Effects = [.. package.Manifest.Effects, package.Manifest.Effects[0]]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK040" &&
            d.Message.Contains("declared more than once", StringComparison.Ordinal));
    }
}
