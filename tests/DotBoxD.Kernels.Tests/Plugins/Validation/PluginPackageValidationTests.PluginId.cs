using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_invalid_manifest_plugin_id_shape()
    {
        var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: PluginAddendumTestPolicies.LongWall());
        var package = FireDamagePluginPackage.Create();
        var invalid = WithPluginId(package, "../fire/damage");

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK050" &&
            d.Message.Contains("plugin id", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    private static PluginPackage WithPluginId(PluginPackage package, string pluginId)
    {
        var metadata = new Dictionary<string, string>(package.Module.Metadata, StringComparer.Ordinal)
        {
            ["pluginId"] = pluginId
        };

        return package with
        {
            Manifest = package.Manifest with { PluginId = pluginId },
            Module = package.Module with { Id = pluginId, Metadata = metadata }
        };
    }
}
