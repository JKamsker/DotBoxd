using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed partial class RemoteRunLocalChainRuntimeTests
{
    [Fact]
    public async Task Local_terminal_install_can_be_uninstalled_by_plugin_or_install_id()
    {
        var package = LowerToPackage(RemoteRunLocalSource);

        using (var server = PluginServer.Create(defaultPolicy: ChainPolicy()))
        {
            var kernel = await server.InstallAsync(package);

            Assert.True(server.Kernels.TryGet(kernel.Manifest.PluginId, out var byPlugin));
            Assert.Same(kernel, byPlugin);
            Assert.True(server.Uninstall(kernel.Manifest.PluginId));
            Assert.True(kernel.IsRevoked);
            Assert.False(server.Kernels.TryGet(kernel.Manifest.PluginId, out _));
        }

        using (var server = PluginServer.Create(defaultPolicy: ChainPolicy()))
        using (var session = server.CreateSession())
        {
            var kernel = await session.InstallAsync(package);

            Assert.True(server.Kernels.TryGet(kernel.InstallId, out var byInstall));
            Assert.Same(kernel, byInstall);
            Assert.True(session.Uninstall(kernel.InstallId));
            Assert.True(kernel.IsRevoked);
            Assert.False(server.Kernels.TryGet(kernel.InstallId, out _));
        }
    }

    [Fact]
    public async Task Local_terminal_setup_retry_reuses_the_owned_callback_route()
    {
        var json = PluginPackageJsonSerializer.Export(LowerToPackage(RemoteRunLocalSource));
        var firstPackage = PluginPackageJsonSerializer.Import(json);
        var retryPackage = PluginPackageJsonSerializer.Import(json);
        var callbackId = firstPackage.CallbackSubscriptionId!;

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: ChainPolicy());
        using var session = server.CreateSession();
        var pushedSubscriptions = new List<string>();
        RemoteLocalPush push = (subscriptionId, _, _) =>
        {
            pushedSubscriptions.Add(subscriptionId);
            return ValueTask.CompletedTask;
        };
        void Wire(InstalledKernel kernel) => server.Hooks.On<ChainAggroEvent>()
            .UseProjecting(kernel, kernel.CallbackSubscriptionId!, push);

        var first = await session.InstallAndWireAsync(firstPackage, Wire);
        var retry = await session.InstallAndWireAsync(retryPackage, Wire);

        Assert.Same(first, retry);
        Assert.Single(server.Kernels.Snapshot(), kernel => kernel.CallbackSubscriptionId == callbackId);

        await server.Hooks.PublishAsync(new ChainAggroEvent("monster-7", 3));

        Assert.Equal(callbackId, Assert.Single(pushedSubscriptions));
    }

    [Fact]
    public async Task Local_terminal_setup_retry_rejects_a_callback_route_collision()
    {
        var package = LowerToPackage(RemoteRunLocalSource);
        var callbackId = package.CallbackSubscriptionId!;
        var collidingPackage = LocalTerminalIdentity.WithCallbackSubscriptionId(
            LowerToPackage(RemoteWholeEventSource),
            callbackId);

        using var server = PluginServer.Create(new InMemoryPluginMessageSink(), defaultPolicy: ChainPolicy());
        using var session = server.CreateSession();
        RemoteLocalPush push = (_, _, _) => ValueTask.CompletedTask;
        void Wire(InstalledKernel kernel) => server.Hooks.On<ChainAggroEvent>()
            .UseProjecting(kernel, kernel.CallbackSubscriptionId!, push);

        await session.InstallAndWireAsync(package, Wire);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await session.InstallAndWireAsync(collidingPackage, Wire));

        Assert.Contains(callbackId, ex.Message, StringComparison.Ordinal);
        Assert.Single(server.Kernels.Snapshot(), kernel => kernel.CallbackSubscriptionId == callbackId);
    }
}
