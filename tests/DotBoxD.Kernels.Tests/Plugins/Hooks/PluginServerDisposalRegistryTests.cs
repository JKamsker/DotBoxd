namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class PluginServerDisposalRegistryTests
{
    private sealed record Ping(string Target, int Value);

    [Fact]
    public async Task Disposed_server_does_not_dispatch_local_hook_or_subscription_handlers()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        var hookCount = 0;
        var subscriptionCount = 0;
        server.Hooks.On<Ping>().RunLocal(_ => hookCount++);
        server.Subscriptions.On<Ping>().RunLocal(_ => subscriptionCount++);

        server.Dispose();

        var hookException = await Record.ExceptionAsync(
            () => server.Hooks.PublishAsync(new Ping("monster-1", 21)).AsTask());
        var subscriptionException = Record.Exception(
            () => server.Subscriptions.Publish(new Ping("monster-1", 21)));

        AssertFailClosed(hookException);
        AssertFailClosed(subscriptionException);
        Assert.Equal(0, hookCount);
        Assert.Equal(0, subscriptionCount);
    }

    private static void AssertFailClosed(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        Assert.IsType<ObjectDisposedException>(exception);
    }
}
