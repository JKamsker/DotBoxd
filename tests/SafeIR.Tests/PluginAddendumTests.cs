using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginAddendumTests
{
    [Fact]
    public void Fire_damage_contracts_live_in_server_abstractions_and_kernel_lives_in_local_plugin()
    {
        Assert.Equal("SafeIR.PluginIpc.Server.Abstractions", typeof(DamageEvent).Assembly.GetName().Name);
        Assert.Equal("SafeIR.PluginLocal", typeof(FireDamageKernel).Assembly.GetName().Name);
        Assert.Same(typeof(FireDamageKernel).Assembly, typeof(FireDamagePluginPackage).Assembly);
    }

    [Fact]
    public async Task Kernel_live_settings_affect_future_hook_runs()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        kernel.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        kernel.Value.DamageType = "ice";
        await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-2"));

        Assert.Equal(2, messages.Messages.Count);
        Assert.Equal("player-1", messages.Messages[0].TargetId);
        Assert.Equal("player-2", messages.Messages[1].TargetId);
    }

    [Fact]
    public async Task Value_binding_filter_updates_without_rebuilding_pipeline()
    {
        var server = PluginServer.Create();
        var minimum = server.BindValue("minimum", 100);
        var handled = 0;
        server.Hooks.On<DamageEvent>()
            .Where((e, _) => e.Amount >= minimum.Value)
            .InvokeKernel((_, _) => handled++);

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        minimum.Value = 200;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        await server.Hooks.PublishAsync(new DamageEvent("fire", 250, "player-1"));

        Assert.Equal(2, handled);
    }

    [Fact]
    public void Context_binding_exposes_typed_live_properties()
    {
        var server = PluginServer.Create();
        var settings = server.BindContext<IFireDamageSettings>(
            "damage",
            value => {
                value.DamageType = "fire";
                value.MinDamage = 100;
            });

        settings.Value.MinDamage = 250;
        settings.Value.DamageType = "ice";

        Assert.Equal("ice", settings.Settings.Get<string>("DamageType"));
        Assert.Equal(250, settings.Settings.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task Unsupported_live_setting_type_reports_local_diagnostic()
    {
        var server = PluginServer.Create();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with {
            Manifest = package.Manifest with {
                LiveSettings = [new LiveSettingDefinition("Anything", "object", null)]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP020");
    }

    [Fact]
    public async Task Live_setting_updates_enforce_manifest_ranges()
    {
        var server = PluginServer.Create();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        var ex = Assert.Throws<SandboxValidationException>(() => kernel.Value.Set("MinDamage", 10_001));

        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP023");
    }

    [Fact]
    public async Task Class_kernel_setting_updates_are_validated_before_execution()
    {
        var server = PluginServer.Create();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.Value.MinDamage = 10_001;

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.Hooks.PublishAsync(new DamageEvent("fire", 20_000, "player-1")).AsTask());
        Assert.Contains(ex.Diagnostics, d => d.Code == "SGP023");
    }

    [Fact]
    public async Task Class_kernel_setting_value_is_stable_for_repeated_gets()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var first = server.Kernels.Get<FireDamageKernel>("fire-damage");
        var second = server.Kernels.Get<FireDamageKernel>("fire-damage");

        first.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Same(first.Value, second.Value);
        Assert.Empty(messages.Messages);
    }

    [Fact]
    public async Task Kernel_handler_capability_is_required_by_policy()
    {
        var server = PluginServer.Create();
        var policy = SandboxPolicyBuilder.Create()
            .WithFuel(10_000)
            .Build();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(FireDamagePluginPackage.Create(), policy).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code is "E-POLICY-CAP" or "E-POLICY-EFFECT");
    }
}
