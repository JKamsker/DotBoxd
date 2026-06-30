using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

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

    [Fact]
    public async Task Class_typed_view_without_parameterless_constructor_fails_closed()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());

        var ex = Assert.Throws<SandboxValidationException>(() =>
            server.Kernels.Get<ConstructorOnlyFireDamageSettings>("fire-damage"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK020");
        Assert.Contains(
            "must be non-abstract and expose a parameterless constructor",
            ex.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Installed_kernel_exposes_typed_live_setting_view_without_registry_lookup()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();

        var typed = installed.As<FireDamageKernel>();

        typed.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Empty(messages.Messages);
        Assert.Same(installed, typed.Kernel);
        Assert.Equal(250, installed.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task Revoked_interface_typed_view_rejects_setting_mutation()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var installed = await server.InstallAsync(FireDamagePluginPackage.Create());
        var settings = installed.As<IFireDamageSettings>();

        Assert.True(server.Uninstall("fire-damage"));

        var ex = Assert.Throws<SandboxRuntimeException>(
            () => settings.Value.MinDamage = 250);

        Assert.Equal(SandboxErrorCode.PolicyDenied, ex.Error.Code);
        Assert.Equal(100, installed.Value.Get<int>("MinDamage"));
    }

    private sealed class AlternateFireDamageSettings
    {
        [LiveSetting]
        public string DamageType { get; set; } = "fire";

        [LiveSetting]
        public int MinDamage { get; set; } = 100;
    }

    private sealed class ConstructorOnlyFireDamageSettings(string damageType)
    {
        [LiveSetting]
        public string DamageType { get; set; } = damageType;

        [LiveSetting]
        public int MinDamage { get; set; } = 100;
    }
}
