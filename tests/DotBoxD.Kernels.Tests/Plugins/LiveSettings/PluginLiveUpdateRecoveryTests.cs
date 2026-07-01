using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PluginLiveUpdateRecoveryTests
{
    [Fact]
    public async Task AsyncSet_flush_recovers_after_later_successful_update()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 10_001;
        await Assert.ThrowsAnyAsync<Exception>(async () => await kernel.FlushUpdatesAsync().AsTask());

        kernel.Value.MinDamage = 250;
        await kernel.FlushUpdatesAsync();

        Assert.Null(kernel.LastAsyncUpdateError);
        Assert.Equal(250, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task AsyncSet_flush_reports_failure_that_completed_before_flush()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 10_001;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        await WaitForAsyncUpdateErrorAsync(kernel);
        kernel.Value.MinDamage = 100;
        await Task.Delay(100);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await kernel.FlushUpdatesAsync().AsTask());

        Assert.NotNull(kernel.LastAsyncUpdateError);
    }

    [Fact]
    public async Task AsyncSet_flush_with_precanceled_token_preserves_completed_failure()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginAddendumTestPolicies.CreateServer(messages);
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 10_001;
        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));
        await WaitForAsyncUpdateErrorAsync(kernel);
        kernel.Value.MinDamage = 100;
        await Task.Delay(100);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await kernel.FlushUpdatesAsync().AsTask());

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await kernel.FlushUpdatesAsync(cancellation.Token).AsTask());
        Assert.NotNull(kernel.LastAsyncUpdateError);
    }

    [Fact]
    public async Task AsyncSet_flush_rejects_revoked_kernel_without_applying_typed_value()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 250;
        kernel.Kernel.Revoke();

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            async () => await kernel.FlushUpdatesAsync().AsTask());

        Assert.Equal(SandboxErrorCode.PolicyDenied, ex.Error.Code);
        Assert.Equal(100, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task AsyncSet_pending_update_does_not_commit_after_revocation()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();
        var kernel = server.Kernels.Get<BlockingReadFireDamageSettings>("fire-damage");

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        kernel.Value.MinDamage = 250;
        kernel.Value.BlockNextRead();

        var publish = server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1")).AsTask();
        await kernel.Value.ReadStarted.WaitAsync(TimeSpan.FromSeconds(5));

        kernel.Kernel.Revoke();
        kernel.Value.ReleaseRead();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsyncUpdateToSettleAsync(kernel);

        Assert.Equal(100, kernel.Kernel.Value.Get<int>("MinDamage"));
        Assert.NotNull(kernel.LastAsyncUpdateError);
    }

    private static async Task WaitForAsyncUpdateErrorAsync(TypedInstalledKernel<FireDamageKernel> kernel)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (kernel.LastAsyncUpdateError is not null)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Async live update did not report a validation failure.");
    }

    private static async Task WaitForAsyncUpdateToSettleAsync(
        TypedInstalledKernel<BlockingReadFireDamageSettings> kernel)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (kernel.LastAsyncUpdateError is not null ||
                kernel.Kernel.Value.Get<int>("MinDamage") == 250)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Async live update did not settle after revocation.");
    }

    private sealed class BlockingReadFireDamageSettings
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blockNextRead;
        private int _minDamage = 100;

        public Task ReadStarted => _readStarted.Task;

        [LiveSetting]
        public int MinDamage
        {
            get
            {
                if (Interlocked.Exchange(ref _blockNextRead, 0) == 1)
                {
                    _readStarted.SetResult();
                    _releaseRead.Task.GetAwaiter().GetResult();
                }

                return Volatile.Read(ref _minDamage);
            }
            set => Volatile.Write(ref _minDamage, value);
        }

        public void BlockNextRead()
            => Volatile.Write(ref _blockNextRead, 1);

        public void ReleaseRead()
            => _releaseRead.SetResult();
    }
}
