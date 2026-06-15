using DotBoxD.Hosting;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests;

/// <summary>
/// Regression coverage for PAL-0044: installed kernels execute plans prepared by their owning
/// <see cref="SandboxHost"/>, so the dispatch hot path can skip the public per-run prepared-plan
/// integrity guard. The internal prepared path must still enforce host-owned runtime boundaries
/// such as capability revocation before any entrypoint can run.
/// </summary>
public sealed class Fix_PAL_0044_Tests
{
    [Fact]
    public async Task Installed_kernel_prepared_fast_path_still_honors_host_capability_revocation()
    {
        var messages = new InMemoryPluginMessageSink();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddLogBindings();
            builder.AddPluginMessageBindings(messages);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var package = FireDamagePluginPackage.Create();
        var plan = await host.PrepareAsync(package.Module, PluginAddendumTestPolicies.LongWall());
        var kernel = new InstalledKernel(host, plan, package, ExecutionMode.Compiled);

        host.RevokeCapability(PluginMessageBindings.CapabilityId, "disabled for tenant");

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(
            async () => await kernel.HandleAsync(
                DamageEventAdapter.Instance,
                new DamageEvent("fire", 120, "player-1")).AsTask());

        Assert.Equal(SandboxErrorCode.PolicyDenied, ex.Error.Code);
        Assert.Empty(messages.Messages);
        var observation = Assert.Single(kernel.ExecutionObservations);
        Assert.False(observation.Succeeded);
        Assert.Equal(ExecutionMode.Compiled, observation.RequestedMode);
        Assert.Equal(ExecutionMode.Compiled, observation.ActualMode);
        Assert.Equal(SandboxErrorCode.PolicyDenied, observation.ErrorCode);
    }
}
