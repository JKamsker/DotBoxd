using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageJsonTests
{
    [Fact]
    public void Import_rejects_local_terminal_without_projected_type()
    {
        var json = JsonDamagePackage().Replace(
            """{ "event": "DamageEvent", "kernel": "JsonDamageKernel" }""",
            """{ "event": "DamageEvent", "kernel": "JsonDamageKernel", "localTerminal": true }""",
            StringComparison.Ordinal);

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Import(json));

        Assert.Contains(ex.Diagnostics, d =>
            d.Code == "DBXK031" &&
            d.Message.Contains("local-terminal", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("projected type", StringComparison.Ordinal));
    }
}
