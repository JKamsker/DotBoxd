using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceBindingInheritanceTests
{
    private const string InheritedServiceSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

        [DotBoxDService]
        public interface IBaseProbeWorld
        {
            [HostCapability("probe.read.value")]
            int GetValue(string id);
        }

        [DotBoxDService]
        public interface IProbeWorld : IBaseProbeWorld;

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("inherited-host-binding-probe")]
        public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetValue(e.TargetId) >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    [Fact]
    public async Task AddBindingsFrom_registers_methods_declared_on_base_service_interfaces()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            InheritedServiceSource,
            "DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.ProbePluginPackage");
        using var server = PluginServer.Create(
            configureHost: AddInheritedProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallAsync(package);

        Assert.Equal("inherited-host-binding-probe", kernel.Manifest.PluginId);
        Assert.Contains("probe.read.value", kernel.Manifest.RequiredCapabilities);
    }

    private static void AddInheritedProbeBindings(SandboxHostBuilder builder)
        => builder.AddBindingsFrom<IDerivedProbeWorld>(new DerivedProbeWorld());

    private static SandboxPolicy ProbeReadPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .Grant("probe.read.*", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private interface IBaseProbeWorld
    {
        [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int GetValue(string id);
    }

    private interface IDerivedProbeWorld : IBaseProbeWorld;

    private sealed class DerivedProbeWorld : IDerivedProbeWorld
    {
        [HostCapability("probe.read.value")]
        public int GetValue(string id) => 42;
    }
}
