using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed class PluginExecutionObservationTests
{
    [Fact]
    public async Task Installed_kernel_exposes_execution_observability_for_each_entrypoint_run()
    {
        var messages = new InMemoryPluginMessageSink();
        var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: PluginAddendumTestPolicies.LongWall(), executionMode: ExecutionMode.Interpreted);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, "player-1"));

        Assert.Equal(2, kernel.ExecutionObservations.Count);
        var shouldHandle = kernel.ExecutionObservations[0];
        Assert.Equal((string?)"ShouldHandle", (string?)shouldHandle.Entrypoint);
        Assert.Equal(ExecutionMode.Interpreted, shouldHandle.RequestedMode);
        Assert.Equal(ExecutionMode.Interpreted, shouldHandle.ActualMode);
        Assert.Equal((string?)"None", (string?)shouldHandle.CacheStatus);
        Assert.Null(shouldHandle.RuntimeForm);

        var handle = kernel.LastExecution;
        Assert.NotNull(handle);
        Assert.Equal((string?)"Handle", (string?)handle.Entrypoint);
        Assert.True((bool)handle.Succeeded);
        Assert.Null<SandboxErrorCode>(handle.FallbackReason);
        Assert.Null(handle.MaterializationStatus);
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    [InlineData(ExecutionMode.Auto)]
    public async Task Server_execution_mode_controls_plugin_dispatch_request(ExecutionMode mode)
    {
        var messages = new InMemoryPluginMessageSink();
        var server = DotBoxD.Plugins.PluginServer.Create(messages, defaultPolicy: PluginAddendumTestPolicies.LongWall(), executionMode: mode);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());
        server.Hooks.On<DamageEvent>().Use<FireDamageKernel>();

        await server.Hooks.PublishAsync(new DamageEvent("fire", 120, $"player-{mode}"));

        Assert.Equal(2, kernel.ExecutionObservations.Count);
        Assert.All(kernel.ExecutionObservations, observation =>
            Assert.Equal(mode, observation.RequestedMode));
    }

    [Fact]
    public async Task Compiled_no_audit_success_still_records_execution_observation()
    {
        var server = PluginAddendumTestPolicies.CreateServer(executionMode: ExecutionMode.Compiled);
        var kernel = await server.InstallAsync(FireDamagePluginPackage.Create());

        var handled = await kernel.ShouldHandleAsync(
            DamageEventAdapter.Instance,
            new DamageEvent("ice", 120, "player-1"));

        Assert.False((bool)handled);
        var observation = Assert.Single(kernel.ExecutionObservations);
        Assert.Equal((string?)"ShouldHandle", (string?)observation.Entrypoint);
        Assert.Equal(ExecutionMode.Compiled, observation.RequestedMode);
        Assert.Equal(ExecutionMode.Compiled, observation.ActualMode);
        Assert.True((bool)observation.Succeeded);
        Assert.Equal((string?)"None", (string?)observation.CacheStatus);
        Assert.Null<SandboxErrorCode>(observation.FallbackReason);
        Assert.False(string.IsNullOrWhiteSpace(observation.ArtifactHash));
    }
}
