using ShaRPC.Core.Server;
using Xunit;

namespace ShaRPC.Tests;

/// <summary>
/// Regression tests for <see cref="InstanceRegistry"/>: the max-instance limit must hold under
/// concurrent registration (not just a racy Count check), and released instances that own resources
/// must be disposed since the registry owns their connection-scoped lifetime.
/// </summary>
public sealed class InstanceRegistryTests
{
    [Fact]
    public void Register_BeyondMax_Throws()
    {
        var registry = new InstanceRegistry(maxInstances: 1);
        registry.Register("svc", new object());

        Assert.Throws<InvalidOperationException>(() => registry.Register("svc", new object()));
    }

    [Fact]
    public async Task Register_ConcurrentlyAtLimit_NeverExceedsMax()
    {
        const int max = 50;
        const int attempts = 250;
        var registry = new InstanceRegistry(max);
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var succeeded = 0;

        var tasks = new Task[attempts];
        for (var i = 0; i < attempts; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    registry.Register("svc", new object());
                    Interlocked.Increment(ref succeeded);
                }
                catch (InvalidOperationException)
                {
                    // Over the limit — expected for the attempts beyond max.
                }
            });
        }

        start.SetResult(true);
        await Task.WhenAll(tasks);

        // The atomic reservation must admit exactly `max` registrations under contention — a plain
        // Count-then-add check would let several racers slip past and exceed the limit.
        Assert.Equal(max, succeeded);
    }

    [Fact]
    public void ReleaseAll_DisposesRegisteredInstances()
    {
        var registry = new InstanceRegistry();
        var disposable = new TrackingDisposable();
        registry.Register("svc", disposable);

        registry.ReleaseAll();

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public void ReleaseAll_DisposesAsyncDisposableInstances()
    {
        var registry = new InstanceRegistry();
        var disposable = new TrackingAsyncDisposable();
        registry.Register("svc", disposable);

        registry.ReleaseAll();

        Assert.True(disposable.Disposed);
    }

    [Fact]
    public void Release_DisposesInstanceAndFreesSlot()
    {
        var registry = new InstanceRegistry(maxInstances: 1);
        var disposable = new TrackingDisposable();
        var id = registry.Register("svc", disposable);

        registry.Release("svc", id);

        Assert.True(disposable.Disposed);
        // Releasing freed the reserved slot, so a fresh registration succeeds against the limit of 1.
        registry.Register("svc", new object());
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
