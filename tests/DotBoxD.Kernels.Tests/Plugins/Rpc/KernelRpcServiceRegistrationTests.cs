namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionRegistrationTests
{
    [Fact]
    public async Task ServerExtension_reuses_registered_proxy()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        await server.RegisterServerExtensionAsync<IMonsterKillerService, BatchKillerKernel>();

        var first = server.ServerExtension<IMonsterKillerService>();
        var second = server.ServerExtension<IMonsterKillerService>();

        Assert.Same(first, second);
        Assert.Equal([new KillResult(1, false), new KillResult(2, true)], first.KillMonsters([1, 2]));
    }

    [Fact]
    public async Task RegisterServerExtension_replaces_cached_proxy()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        await server.RegisterServerExtensionAsync<IMonsterKillerService, BatchKillerKernel>();
        var first = server.ServerExtension<IMonsterKillerService>();

        await server.RegisterServerExtensionAsync<IMonsterKillerService, BatchKillerKernel>();
        var second = server.ServerExtension<IMonsterKillerService>();

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Uninstall_clears_rpc_service_registration()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var pluginId = await server.RegisterServerExtensionAsync<IMonsterKillerService, BatchKillerKernel>();

        Assert.True(server.Uninstall(pluginId));

        var ex = Assert.Throws<InvalidOperationException>(
            () => server.ServerExtension<IMonsterKillerService>());
        Assert.Contains("No server extension is registered", ex.Message);
    }

    [Fact]
    public async Task Direct_replacement_clears_rpc_service_registration()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        await server.RegisterServerExtensionAsync<IMonsterKillerService, BatchKillerKernel>();

        await server.InstallServerExtensionAsync(RpcKernelTestPackages.MonsterKiller());

        var ex = Assert.Throws<InvalidOperationException>(
            () => server.ServerExtension<IMonsterKillerService>());
        Assert.Contains("No server extension is registered", ex.Message);
    }
}
