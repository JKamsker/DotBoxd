using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.PluginLocal;
using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PluginExecutionObservationTests
{
    [Fact]
    public async Task Installed_kernel_exposes_execution_observability_for_each_entrypoint_run()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = PluginServer.Create(messages, executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().UseKernel<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Equal(2, kernel.ExecutionObservations.Count);
        var shouldHandle = kernel.ExecutionObservations[0];
        Assert.Equal("ShouldHandle", shouldHandle.Entrypoint);
        Assert.Equal(ExecutionMode.Interpreted, shouldHandle.RequestedMode);
        Assert.Equal(ExecutionMode.Interpreted, shouldHandle.ActualMode);
        Assert.Equal("None", shouldHandle.CacheStatus);
        Assert.Null(shouldHandle.RuntimeForm);

        var handle = kernel.LastExecution;
        Assert.NotNull(handle);
        Assert.Equal("Handle", handle.Entrypoint);
        Assert.True(handle.Succeeded);
        Assert.Null(handle.FallbackReason);
        Assert.Null(handle.MaterializationStatus);
    }
}
