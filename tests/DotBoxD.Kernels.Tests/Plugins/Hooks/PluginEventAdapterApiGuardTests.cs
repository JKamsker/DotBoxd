using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class PluginEventAdapterApiGuardTests
{
    [Fact]
    public void RegisterEventAdapter_rejects_null_adapter()
    {
        var server = PluginServer.Create();

        var ex = Assert.Throws<ArgumentNullException>(
            () => server.RegisterEventAdapter<AdapterGuardEvent>(null!));

        Assert.Equal("adapter", ex.ParamName);
    }

    [Fact]
    public void Hooks_On_rejects_null_adapter()
    {
        var server = PluginServer.Create();

        var ex = Assert.Throws<ArgumentNullException>(
            () => server.Hooks.On<AdapterGuardEvent>(null!));

        Assert.Equal("adapter", ex.ParamName);
    }

    [Fact]
    public void Subscriptions_On_rejects_null_adapter()
    {
        var server = PluginServer.Create();

        var ex = Assert.Throws<ArgumentNullException>(
            () => server.Subscriptions.On<AdapterGuardEvent>(null!));

        Assert.Equal("adapter", ex.ParamName);
    }

    private sealed record AdapterGuardEvent;
}
