using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class LiveStateSyncRegistryTests
{
    [Fact]
    public async Task Register_during_input_sync_does_not_throw()
    {
        var registry = new LiveStateSyncRegistry(_ => LiveUpdateMode.Sync);
        var syncStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSync = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        registry.Register(typeof(FirstState), () =>
        {
            syncStarted.SetResult();
            releaseSync.Task.GetAwaiter().GetResult();
        });

        var sync = Task.Run(() => registry.SynchronizeForInput());
        await syncStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        registry.Register(typeof(SecondState), () => { });
        releaseSync.SetResult();

        Assert.Empty(await sync.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class FirstState;
    private sealed class SecondState;
}
