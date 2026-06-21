using System.Reflection;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Plugins.Rpc;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins;

/// <summary>
/// Covers the session-ownership model: a kernel is owned by the session that installed it, cannot be
/// hijacked or tuned by another session, and is revoked + unregistered when its session is disposed
/// (the server-side equivalent of "the plugin disconnected").
/// </summary>
public sealed class PluginOwnershipTests
{
    [Fact]
    public async Task Cross_owner_id_reuse_is_rejected_fail_closed()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var ownerA = server.CreateSession();
        var ownerB = server.CreateSession();
        var kernelA = await ownerA.InstallAsync(FireDamagePluginPackage.Create());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await ownerB.InstallAsync(FireDamagePluginPackage.Create()).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK060");
        Assert.False((bool)kernelA.IsRevoked);
        Assert.True((bool)server.Kernels.TryGet("fire-damage", out var current));
        Assert.Same(kernelA, current);
    }

    [Fact]
    public async Task Session_dispose_revokes_and_unregisters_owned_kernels()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        var kernel = await session.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True((bool)server.Kernels.TryGet("fire-damage", out _));

        session.Dispose();

        Assert.True((bool)kernel.IsRevoked);
        Assert.False((bool)server.Kernels.TryGet("fire-damage", out _));
    }

    [Fact]
    public async Task Same_owner_reinstall_replaces_and_revokes_prior()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        var first = await session.InstallAsync(FireDamagePluginPackage.Create());
        var second = await session.InstallAsync(FireDamagePluginPackage.Create());

        Assert.True((bool)first.IsRevoked);
        Assert.False((bool)second.IsRevoked);
        Assert.True((bool)server.Kernels.TryGet("fire-damage", out var current));
        Assert.Same(second, current);
    }

    [Fact]
    public async Task UpdateSettings_rejects_a_plugin_id_the_session_does_not_own()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var owner = server.CreateSession();
        await owner.InstallAsync(FireDamagePluginPackage.Create());
        var intruder = server.CreateSession();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await intruder
                .UpdateSettingsAsync("fire-damage", new Dictionary<string, object?> { ["DamageType"] = "ice" })
                .AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK061");
    }

    [Fact]
    public async Task Stale_session_owner_cannot_update_reused_plugin_id()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var ownerA = server.CreateSession();
        await ownerA.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Uninstall("fire-damage"));

        var ownerB = server.CreateSession();
        var replacement = await ownerB.InstallAsync(FireDamagePluginPackage.Create());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await ownerA
                .UpdateSettingsAsync("fire-damage", new Dictionary<string, object?> { ["DamageType"] = "ice" })
                .AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK061");
        Assert.Equal("fire", replacement.Value.Get<string>("DamageType"));
    }

    [Fact]
    public async Task Session_dispose_waits_for_in_flight_owned_setting_update()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        var kernel = await session.InstallAsync(FireDamagePluginPackage.Create());
        var definition = Assert.Single(kernel.Value.Definitions, s => s.Name == "DamageType");
        var setting = new BlockingLiveSetting(definition, "fire");
        ReplaceSetting(kernel.Value, setting);
        var update = Task.Run(async () => await session.UpdateSettingsAsync(
            "fire-damage",
            new Dictionary<string, object?> { ["DamageType"] = "ice" }).AsTask());
        Task? dispose = null;
        var disposedBeforeUpdateCompleted = false;
        try
        {
            var updateReachedSetting = await Task.WhenAny(setting.Started, update)
                .WaitAsync(TimeSpan.FromSeconds(5)) == setting.Started;
            if (!updateReachedSetting)
            {
                await update;
                Assert.Fail("The live setting update completed before reaching the blocking setting.");
            }

            var disposeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispose = Task.Run(() =>
            {
                disposeStarted.SetResult();
                session.Dispose();
            });
            await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposedBeforeUpdateCompleted =
                await Task.WhenAny(dispose, Task.Delay(250)) == dispose;
        }
        finally
        {
            setting.Release();
        }

        await update.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose is not null)
        {
            await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.False(disposedBeforeUpdateCompleted);
        Assert.Equal("ice", setting.CurrentValue);
        Assert.True((bool)kernel.IsRevoked);
    }

    [Fact]
    public async Task Disposed_session_rejects_further_installs()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var session = server.CreateSession();
        session.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await session.InstallAsync(FireDamagePluginPackage.Create()).AsTask());
    }

    [Fact]
    public async Task Owner_with_null_id_keeps_legacy_replace_semantics()
    {
        // Direct (sessionless) installs have no owner; reusing an id replaces + revokes, as before.
        using var server = DotBoxD.Plugins.PluginServer.Create(defaultPolicy: LongWallPluginPolicy());
        var first = await server.InstallAsync(FireDamagePluginPackage.Create());
        var second = await server.InstallAsync(FireDamagePluginPackage.Create());

        Assert.True((bool)first.IsRevoked);
        Assert.False((bool)second.IsRevoked);
    }

    [Fact]
    public async Task Session_rpc_install_cannot_replace_server_owned_rpc_kernel()
    {
        using var server = CreateRpcServer();
        var serverKernel = await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());
        var session = server.CreateSession();

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await session.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller()).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK060");
        Assert.False((bool)serverKernel.IsRevoked);
        Assert.True((bool)server.Kernels.TryGet("monster-killer", out var current));
        Assert.Same(serverKernel, current);
        Assert.False(session.Owns("monster-killer"));
    }

    [Fact]
    public async Task Server_rpc_install_cannot_replace_session_owned_rpc_kernel()
    {
        using var server = CreateRpcServer();
        var session = server.CreateSession();
        var sessionKernel = await session.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller()).AsTask());

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK060");
        Assert.False((bool)sessionKernel.IsRevoked);
        Assert.True(session.Owns("monster-killer"));
        Assert.True((bool)server.Kernels.TryGet("monster-killer", out var current));
        Assert.Same(sessionKernel, current);
    }

    private static DotBoxD.Plugins.PluginServer CreateRpcServer()
        => DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());

    private static SandboxPolicy LongWallPluginPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static void ReplaceSetting(LiveSettingStore store, ILiveSetting setting)
    {
        var field = typeof(LiveSettingStore).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var settings = (Dictionary<string, ILiveSetting>)field.GetValue(store)!;
        settings[setting.Name] = setting;
    }

    private sealed class BlockingLiveSetting(
        LiveSettingDefinition definition,
        object? initialValue) : ILiveSetting
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private object? _value = initialValue;

        public Task Started => _started.Task;
        public string Name => definition.Name;
        public LiveSettingDefinition Definition => definition;
        public object? CurrentValue => _value;

        public SandboxValue ToSandboxValue()
            => SandboxValue.FromString((string)_value!);

        public void SetObject(object? value)
        {
            _started.SetResult();
            _release.Task.GetAwaiter().GetResult();
            _value = value;
        }

        public void Release()
            => _release.TrySetResult();
    }
}
