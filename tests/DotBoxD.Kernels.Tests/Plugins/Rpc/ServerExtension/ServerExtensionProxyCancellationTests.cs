using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionProxyCancellationTests
{
    [Fact]
    public async Task Runtime_proxy_treats_trailing_cancellation_token_as_transport_token()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ServerExtensionProxyTests.MonsterKillerWithGeneratedClientSource,
            "Sample.MonsterKillerPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: RpcKernelTestPackages.AddKillBinding,
            defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var service = ServerExtensionProxy.Create<ICancellableMonsterKillerService>(kernel);

        var results = await service.KillMonstersAsync([4, 5], CancellationToken.None);

        Assert.Equal([new KillResult(4, true), new KillResult(5, false)], results);
    }

    private interface ICancellableMonsterKillerService
    {
        ValueTask<List<KillResult>> KillMonstersAsync(
            List<int> monsterIds,
            CancellationToken cancellationToken = default);
    }
}
