using DotBoxd.Kernels.PluginIpc.Server.Abstractions;
using DotBoxd.Kernels.PluginLocal;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

public sealed class PluginAddendumTests
{
    [Fact]
    public void Fire_damage_contracts_live_in_server_abstractions_and_kernel_lives_in_local_plugin()
    {
        Assert.Equal("DotBoxd.Kernels.PluginIpc.Server.Abstractions", typeof(DamageEvent).Assembly.GetName().Name);
        Assert.Equal("DotBoxd.Kernels.PluginLocal", typeof(FireDamageKernel).Assembly.GetName().Name);
        Assert.Same(typeof(FireDamageKernel).Assembly, typeof(FireDamagePluginPackage).Assembly);
    }

    [Fact]
    public async Task Kernel_live_settings_affect_future_hook_runs()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
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
    public async Task Runtime_configuration_example_settings_are_declared_by_fire_damage_package()
    {
        var settings = new Dictionary<string, object?>
        {
            ["DamageType"] = "ice",
            ["MinDamage"] = 250
        };
        var package = FireDamagePluginPackage.Create();
        var declared = package.Manifest.LiveSettings
            .Select(setting => setting.Name)
            .ToHashSet(StringComparer.Ordinal);
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        var installed = await server.InstallAsync(package);

        Assert.All(settings.Keys, key => Assert.Contains(key, declared));
        await installed.ModifySettingsAsync(settings);

        Assert.Equal("ice", installed.Value.Get<string>("DamageType"));
        Assert.Equal(250, installed.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task Direct_kernel_value_updates_are_full_sync_by_default()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        Assert.Equal(LiveUpdateMode.Sync, kernel.UpdateMode);
        kernel.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Empty(messages.Messages);
        Assert.Equal(250, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task AsyncSet_direct_kernel_value_updates_do_not_wait_for_server_ack()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        await kernel.FlushUpdatesAsync();
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-2"));

        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
        Assert.Equal(250, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task AsyncSet_flush_pushes_direct_kernel_value_updates_before_next_run()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 250;
        await kernel.FlushUpdatesAsync();
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Empty(messages.Messages);
        Assert.Equal(250, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task ModifyAsync_ignores_update_mode_and_waits_for_batch_commit()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        await kernel.ModifyAsync(state => state.MinDamage = 250);
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Empty(messages.Messages);
        Assert.Equal(250, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task ModifyAsync_commits_class_kernel_settings_as_a_batch()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        await kernel.ModifyAsync(state =>
        {
            state.DamageType = "ice";
            state.MinDamage = 250;
        });

        await server.Hooks.PublishAsync(new DamageEvent("fire", 300, "player-1"));
        await server.Hooks.PublishAsync(new DamageEvent("ice", 200, "player-2"));
        await server.Hooks.PublishAsync(new DamageEvent("ice", 300, "player-3"));

        Assert.Equal("ice", kernel.Value.DamageType);
        Assert.Equal(250, kernel.Value.MinDamage);
        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-3", message.TargetId);
    }

    [Fact]
    public async Task ModifyAsync_rejects_invalid_batch_without_changing_current_settings()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await kernel.ModifyAsync(state =>
            {
                state.DamageType = "ice";
                state.MinDamage = 10_001;
            }).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK023");
        Assert.Equal("fire", kernel.Value.DamageType);
        Assert.Equal(100, kernel.Value.MinDamage);
        Assert.Equal("fire", kernel.Kernel.Value.Get<string>("DamageType"));
        Assert.Equal(100, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task ModifyAsync_supports_interface_shaped_kernel_settings()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var settings = server.Kernels.Get<IFireDamageSettings>("fire-damage");

        await settings.ModifyAsync(state =>
        {
            state.DamageType = "ice";
            state.MinDamage = 250;
        });

        Assert.Equal("ice", settings.Value.DamageType);
        Assert.Equal(250, settings.Value.MinDamage);
        Assert.Equal("ice", settings.Kernel.Value.Get<string>("DamageType"));
        Assert.Equal(250, settings.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task ModifyAsync_atomic_waits_for_in_flight_kernel_execution()
    {
        var messages = new BlockingPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        var publish = server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1")).AsTask();
        await messages.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var modify = kernel.ModifyAsync(state => state.MinDamage = 250, atomic: true).AsTask();
        var completed = await Task.WhenAny(modify, Task.Delay(100));

        Assert.NotSame(modify, completed);
        messages.ReleaseSend.SetResult();
        await publish;
        await modify;
        Assert.Equal(250, kernel.Value.MinDamage);
    }

    [Fact]
    public async Task Value_binding_filter_updates_without_rebuilding_pipeline()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var minimum = server.BindValue("minimum", 100);
        var handled = 0;
        server.Hooks.On<DamageEvent>()
            .Where((e, _) => e.Amount >= minimum.Value)
            .InvokeHostHandler((_, _) => handled++);

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        minimum.Value = 200;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        await server.Hooks.PublishAsync(new DamageEvent("fire", 250, "player-1"));

        Assert.Equal(2, handled);
    }

    [Fact]
    public void Context_binding_exposes_typed_live_properties()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var settings = server.BindContext<IFireDamageSettings>(
            "damage",
            value =>
            {
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
        var server = PluginAddendumTestPolicies.CreateServer();
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Manifest = package.Manifest with
            {
                LiveSettings = [new LiveSettingDefinition("Anything", "object", null)]
            }
        };

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () => await server.InstallAsync(invalid).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK020");
    }

    [Fact]
    public async Task Live_setting_updates_enforce_manifest_ranges()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        var ex = Assert.Throws<SandboxValidationException>(() => kernel.Value.Set("MinDamage", 10_001));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK023");
    }

    [Fact]
    public async Task Class_kernel_setting_updates_are_validated_before_execution()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.Value.MinDamage = 10_001;

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.Hooks.PublishAsync(new DamageEvent("fire", 20_000, "player-1")).AsTask());
        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK023");
    }

    [Fact]
    public async Task Class_kernel_setting_value_is_stable_for_repeated_gets()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();
        var first = server.Kernels.Get<FireDamageKernel>("fire-damage");
        var second = server.Kernels.Get<FireDamageKernel>("fire-damage");

        first.Value.MinDamage = 250;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Same(first.Value, second.Value);
        Assert.Empty(messages.Messages);
    }
}
