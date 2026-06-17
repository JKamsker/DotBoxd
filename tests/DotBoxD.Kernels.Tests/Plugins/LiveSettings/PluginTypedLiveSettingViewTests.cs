using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PluginTypedLiveSettingViewTests
{
    [Fact]
    public async Task Stale_class_typed_view_does_not_overwrite_newer_view_on_input_sync()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();

        var primary = server.Kernels.Get<FireDamageKernel>("fire-damage");
        _ = server.Kernels.Get<AlternateFireDamageSettings>("fire-damage");

        primary.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Empty(messages.Messages);
        Assert.Equal(250, primary.Kernel.Value.Get<int>("MinDamage"));
    }

    private sealed class AlternateFireDamageSettings
    {
        [LiveSetting]
        public string DamageType { get; set; } = "fire";

        [LiveSetting]
        public int MinDamage { get; set; } = 100;
    }
}
