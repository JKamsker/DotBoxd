namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class HookPipelineCancellationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    private sealed record Ping(string Target, int Value);

    [Fact]
    public async Task Precancelled_publish_does_not_run_filters_or_local_handlers()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        var filterInvoked = false;
        var handlerInvoked = false;
        server.Hooks.On<Ping>()
            .Where((_, _) => { filterInvoked = true; return true; })
            .RunLocal((_, _) => handlerInvoked = true);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => server.Hooks.PublishAsync(new Ping("monster-1", 21), cts.Token).AsTask());

        Assert.False(filterInvoked);
        Assert.False(handlerInvoked);
    }

    [Fact]
    public async Task Cancellation_after_async_filter_starts_waits_for_filter_and_skips_later_stages()
    {
        using var server = DotBoxD.Plugins.PluginServer.Create();
        using var cts = new CancellationTokenSource();
        var releaseFilter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var filterCompleted = false;
        var nextFilterInvoked = false;
        var handlerInvoked = false;
        server.Hooks.On<Ping>()
            .Where(async (_, _) =>
            {
                cts.Cancel();
                await releaseFilter.Task.WaitAsync(TestTimeout);
                filterCompleted = true;
                return true;
            })
            .Where((_, _) => { nextFilterInvoked = true; return true; })
            .RunLocal((_, _) => handlerInvoked = true);

        var publish = server.Hooks.PublishAsync(new Ping("monster-1", 21), cts.Token).AsTask();
        await Task.Yield();

        Assert.False(publish.IsCompleted);
        releaseFilter.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => publish);

        Assert.True(filterCompleted);
        Assert.False(nextFilterInvoked);
        Assert.False(handlerInvoked);
    }
}
