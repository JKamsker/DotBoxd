namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class SubscriptionPipelineCancellationTests
{
    private sealed record Ping(string Target, int Value);

    [Fact]
    public void Precancelled_publish_does_not_run_filters_or_local_handlers()
    {
        var subscription = CreateSubscriptionProbe();
        using var server = subscription.Server;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = Record.Exception(
            () => server.Subscriptions.Publish(new Ping("monster-1", 21), cts.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.False(subscription.FilterInvoked());
        Assert.False(subscription.HandlerInvoked());
    }

    [Fact]
    public async Task Precancelled_publish_async_does_not_run_filters_or_local_handlers()
    {
        var subscription = CreateSubscriptionProbe();
        using var server = subscription.Server;

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = await Record.ExceptionAsync(
            async () => await server.Subscriptions.PublishAsync(new Ping("monster-1", 21), cts.Token).AsTask());

        Assert.IsType<OperationCanceledException>(exception);
        Assert.False(subscription.FilterInvoked());
        Assert.False(subscription.HandlerInvoked());
    }

    private static (
        DotBoxD.Plugins.PluginServer Server,
        Func<bool> FilterInvoked,
        Func<bool> HandlerInvoked) CreateSubscriptionProbe()
    {
        var server = DotBoxD.Plugins.PluginServer.Create();
        var filterInvoked = false;
        var handlerInvoked = false;
        server.Subscriptions.On<Ping>()
            .Where((_, _) => { filterInvoked = true; return true; })
            .RunLocal((_, _) => handlerInvoked = true);

        return (server, () => filterInvoked, () => handlerInvoked);
    }
}
