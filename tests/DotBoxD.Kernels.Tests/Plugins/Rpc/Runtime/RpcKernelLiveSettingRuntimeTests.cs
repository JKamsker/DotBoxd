using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelLiveSettingRuntimeTests
{
    [Fact]
    public async Task Rpc_invocation_synchronizes_class_typed_live_settings_before_building_input()
    {
        var package = CreateThresholdPackage("return Threshold;");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var settings = server.Kernels.Get<ThresholdSettings>(package.Manifest.PluginId);

        settings.Value.Threshold = 9;

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(9, Assert.IsType<I32Value>(result).Value);
    }

    [Fact]
    public async Task Rpc_invocation_synchronizes_this_qualified_live_settings_before_building_input()
    {
        var package = CreateThresholdPackage("return this.Threshold;");
        using var server = PluginServer.Create();
        var kernel = await server.InstallServerExtensionAsync(package);
        var settings = server.Kernels.Get<ThresholdSettings>(package.Manifest.PluginId);

        settings.Value.Threshold = 9;

        var result = await kernel.InvokeServerExtensionAsync([]);

        Assert.Equal(9, Assert.IsType<I32Value>(result).Value);
    }

    private static PluginPackage CreateThresholdPackage(string readStatement)
        => PluginAnalyzerGeneratedPackageFactory.Create(
            $$"""
            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;

            namespace Sample;

            [ServerExtension("threshold")]
            public sealed partial class ThresholdKernel
            {
                [LiveSetting]
                public int Threshold { get; set; } = 5;

                public int Read(HookContext ctx)
                {
                    {{readStatement}}
                }
            }
            """,
            "Sample.ThresholdPluginPackage");

    private sealed class ThresholdSettings
    {
        public int Threshold { get; set; }
    }
}
