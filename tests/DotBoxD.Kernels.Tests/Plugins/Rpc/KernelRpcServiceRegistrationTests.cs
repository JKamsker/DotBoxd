namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionRegistrationTests
{
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
