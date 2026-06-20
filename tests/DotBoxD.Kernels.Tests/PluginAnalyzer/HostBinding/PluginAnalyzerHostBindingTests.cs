using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;
using static DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.PluginAnalyzerHostBindingTestSupport;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

/// <summary>
/// End-to-end coverage of host-binding-call lowering: a kernel reaching a host service through
/// <c>ctx.Host&lt;T&gt;()</c> whose method carries <c>[HostBinding(id, capability)]</c> lowers to a
/// sandbox <c>CallExpression(id, …)</c>, the capability is derived into the manifest, and the install
/// is gated on the policy granting that capability (wildcard-aware, fail-closed). Also covers a
/// <c>[Capability]</c>-gated event-property read contributing its capability.
/// </summary>
public sealed class PluginAnalyzerHostBindingTests
{
    private const string ProbeSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);

            [HostBinding("host.probe.getSecret", "probe.admin.secret", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetSecret(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("host-binding-probe")]
        public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetValue(e.TargetId) >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    private const string SecretSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getSecret", "probe.admin.secret", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetSecret(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("host-binding-secret")]
        public sealed partial class SecretKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetSecret(e.TargetId) >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    private const string InjectedProbeSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("injected-host-binding-probe")]
        public sealed partial class InjectedProbeKernel : IEventKernel<ProbeEvent>
        {
            private readonly IProbeWorld _world;

            public InjectedProbeKernel(IProbeWorld world) => _world = world;

            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => _world.GetValue(e.TargetId) >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

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
    public void Host_binding_call_derives_its_capability_into_the_manifest()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");

        Assert.Contains("probe.read.value", package.Manifest.RequiredCapabilities);
        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);
        // GetSecret is declared on the interface but never called, so its capability is not required.
        Assert.DoesNotContain("probe.admin.secret", package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task Host_binding_kernel_installs_and_runs_under_a_wildcard_grant()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");
        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallAsync(package);
        Assert.Equal((string?)"host-binding-probe", (string?)kernel.Manifest.PluginId);

        var adapter = new ProbeEventAdapter();
        // The host.probe.getValue binding returns 42.
        Assert.True((bool)await kernel.ShouldHandleAsync(adapter, new ProbeEvent("player-1", "hi", 10)));
        Assert.False((bool)await kernel.ShouldHandleAsync(adapter, new ProbeEvent("player-1", "hi", 50)));
    }

    [Fact]
    public async Task Host_binding_call_can_use_a_constructor_injected_service_field()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            InjectedProbeSource, "Sample.InjectedProbePluginPackage");
        Assert.Contains("probe.read.value", package.Manifest.RequiredCapabilities);

        using var server = PluginServer.Create(
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallAsync(package);
        var adapter = new ProbeEventAdapter();

        Assert.True(await kernel.ShouldHandleAsync(adapter, new ProbeEvent("player-1", "hi", 10)));
        Assert.False(await kernel.ShouldHandleAsync(adapter, new ProbeEvent("player-1", "hi", 50)));
    }

    [Fact]
    public async Task Host_binding_kernel_installs_through_json_export_import()
    {
        // Mirrors the GameServer example path: the plugin exports verified IR to JSON and the server
        // imports + installs it, exercising host-binding CallExpression + requiredCapabilities round-trip.
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");
        var json = PluginPackageJsonSerializer.Export(package, indented: true);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallJsonAsync(json);
        Assert.Equal("host-binding-probe", kernel.Manifest.PluginId);
        Assert.Contains("probe.read.value", kernel.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task Host_binding_kernel_is_denied_when_its_capability_is_not_granted()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: AddProbeBindings,
            defaultPolicy: MessageWriteOnlyPolicy());

        // probe.read.value is never granted → preparation fails closed.
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await server.InstallAsync(package).AsTask());
    }

    [Fact]
    public async Task Wildcard_grant_does_not_cross_capability_subtrees()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(SecretSource, "Sample.SecretPluginPackage");
        using var server = DotBoxD.Plugins.PluginServer.Create(
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        // The probe.read.* grant covers probe.read.value but not probe.admin.secret.
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await server.InstallAsync(package).AsTask());
    }

    [Fact]
    public async Task Guardian_shaped_kernel_with_live_settings_and_host_binding_installs_via_json()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            GuardianReplicaSource, "Sample.GuardianReplicaPluginPackage");
        var json = PluginPackageJsonSerializer.Export(package, indented: true);

        var messages = new InMemoryPluginMessageSink();
        using var server = DotBoxD.Plugins.PluginServer.Create(
            messages,
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallJsonAsync(json);
        Assert.Equal("guardian-replica", kernel.Manifest.PluginId);
    }

    [Fact]
    public void Gated_event_property_read_derives_its_capability()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(GatedPropertySource, "Sample.GatedPluginPackage");

        Assert.Contains("event.read.health", package.Manifest.RequiredCapabilities);
        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);
    }
}
