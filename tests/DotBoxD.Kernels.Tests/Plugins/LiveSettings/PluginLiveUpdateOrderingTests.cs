using System.Reflection;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PluginLiveUpdateOrderingTests
{
    [Fact]
    public async Task AsyncSet_flush_keeps_newer_typed_value_after_older_pending_update()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        var releaseStaleUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        PendingQueue(kernel.Kernel).Enqueue(() =>
        {
            releaseStaleUpdate.Task.GetAwaiter().GetResult();
            kernel.Kernel.Value.Set("MinDamage", 250);
        });

        kernel.Value.MinDamage = 300;
        var flush = kernel.FlushUpdatesAsync().AsTask();
        releaseStaleUpdate.SetResult();

        await flush.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(300, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    private static PendingLiveUpdateQueue PendingQueue(InstalledKernel kernel)
    {
        var field = typeof(InstalledKernel).GetField(
            "_pendingLiveUpdates",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (PendingLiveUpdateQueue)field.GetValue(kernel)!;
    }
}
