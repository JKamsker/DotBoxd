using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginRevocationTests
{
    [Fact]
    public async Task Uninstall_revokes_existing_hook_pipeline_kernel_reference()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        var removed = server.Uninstall("fire-damage");
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-2"));

        Assert.True(removed);
        Assert.True(kernel.IsRevoked);
        var message = Assert.Single(messages.Messages);
        Assert.Equal("player-1", message.TargetId);
    }

    [Fact]
    public async Task Revoked_kernel_handle_rejects_direct_execution()
    {
        var server = PluginServer.Create();
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        Assert.True(server.Uninstall("fire-damage"));

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            async () => await kernel.HandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1")).AsTask());

        Assert.Equal(SandboxErrorCode.PolicyDenied, ex.Error.Code);
    }
}
