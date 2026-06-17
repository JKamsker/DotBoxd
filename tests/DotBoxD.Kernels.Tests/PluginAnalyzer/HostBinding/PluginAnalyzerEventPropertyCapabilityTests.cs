using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class PluginAnalyzerEventPropertyCapabilityTests
{
    private const string GatedPropertySource = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record GatedEvent(
            string TargetId,
            string Message,
            [property: Capability("event.read.health")] int Health);

        [Plugin("gated-property")]
        public sealed partial class GatedKernel : IEventKernel<GatedEvent>
        {
            public bool ShouldHandle(GatedEvent e, HookContext ctx) => e.Health > 0;

            public void Handle(GatedEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    [Fact]
    public async Task Gated_event_property_kernel_installs_under_matching_policy_grant()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            GatedPropertySource,
            "Sample.GatedPluginPackage");

        using var server = PluginServer.Create(
            new InMemoryPluginMessageSink(),
            defaultPolicy: SandboxPolicyBuilder.Create()
                .GrantLogging()
                .GrantHostMessageWrite()
                .Grant("event.read.*", new { }, SandboxEffect.None)
                .WithFuel(100_000)
                .WithMaxHostCalls(1_000)
                .Build());

        var kernel = await server.InstallAsync(package);

        Assert.Equal("gated-property", kernel.Manifest.PluginId);
    }
}
