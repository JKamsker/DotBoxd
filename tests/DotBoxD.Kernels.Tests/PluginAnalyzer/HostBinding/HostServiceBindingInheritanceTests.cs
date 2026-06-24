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
            [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
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

    private const string ExplicitEffectSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

        [DotBoxDService]
        public interface IExplicitEffectProbeWorld
        {
            [HostCapability("probe.action.patch", HostBindingEffect.HostStateWrite)]
            int PatchValue(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message);

        [Plugin("explicit-effect-probe")]
        public sealed partial class ExplicitEffectKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IExplicitEffectProbeWorld>().PatchValue(e.TargetId) > 0;

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

    [Fact]
    public void AddBindingsFrom_rejects_nullable_value_types_in_service_contracts()
    {
        var builder = new SandboxHostBuilder();

        Assert.Throws<NotSupportedException>(
            () => builder.AddBindingsFrom<INullableProbeWorld>(new NullableProbeWorld()));
    }

    [Theory]
    [MemberData(nameof(InvalidHostCapabilityCases))]
    public void AddBindingsFrom_rejects_invalid_host_capability_effects(
        Action<SandboxHostBuilder> configure,
        string expectedMessage)
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<InvalidOperationException>(() => configure(builder));

        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auto_binding_effects_come_from_interface_metadata_not_method_name_or_implementation()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ExplicitEffectSource,
            "DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.ExplicitEffectPluginPackage");
        Assert.Contains("HostStateWrite", package.Manifest.Effects);
        Assert.DoesNotContain("HostStateRead", package.Manifest.Effects);

        using var server = PluginServer.Create(
            configureHost: builder => builder.AddBindingsFrom<IExplicitEffectProbeWorld>(new ExplicitEffectProbeWorld()),
            defaultPolicy: ProbeWritePolicy());

        var kernel = await server.InstallAsync(package);

        Assert.Equal("explicit-effect-probe", kernel.Manifest.PluginId);
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

    private static SandboxPolicy ProbeWritePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .Grant("probe.action.patch", new { }, SandboxEffect.HostStateWrite)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private interface IBaseProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int GetValue(string id);
    }

    private interface IDerivedProbeWorld : IBaseProbeWorld;

    private sealed class DerivedProbeWorld : IDerivedProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        public int GetValue(string id) => 42;
    }

    private interface INullableProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        [HostBinding("host.probe.echo", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Echo(int? value);
    }

    private sealed class NullableProbeWorld : INullableProbeWorld
    {
        [HostCapability("probe.read.value", HostBindingEffect.HostStateRead)]
        public int Echo(int? value) => value.GetValueOrDefault();
    }

    private interface IExplicitEffectProbeWorld
    {
        [HostCapability("probe.action.patch", HostBindingEffect.HostStateWrite)]
        int PatchValue(string id);
    }

    private sealed class ExplicitEffectProbeWorld : IExplicitEffectProbeWorld
    {
        [HostCapability("probe.action.patch", HostBindingEffect.HostStateRead)]
        public int PatchValue(string id) => 1;
    }

    public static TheoryData<Action<SandboxHostBuilder>, string> InvalidHostCapabilityCases()
        => new()
        {
            {
                builder => builder.AddBindingsFrom<INoAccessProbeWorld>(new NoAccessProbeWorld()),
                "must declare exactly one of HostStateRead or HostStateWrite"
            },
            {
                builder => builder.AddBindingsFrom<IBothAccessProbeWorld>(new BothAccessProbeWorld()),
                "must declare exactly one of HostStateRead or HostStateWrite"
            },
            {
                builder => builder.AddBindingsFrom<IMissingAllocProbeWorld>(new MissingAllocProbeWorld()),
                "must declare Allocates because its return shape allocates"
            },
            {
                builder => builder.AddBindingsFrom<IExtraAllocProbeWorld>(new ExtraAllocProbeWorld()),
                "must not declare Allocates because its return shape does not allocate"
            }
        };

    private interface INoAccessProbeWorld
    {
        [HostCapability("probe.bad.none", HostBindingEffect.None)]
        int Read();
    }

    private sealed class NoAccessProbeWorld : INoAccessProbeWorld
    {
        public int Read() => 1;
    }

    private interface IBothAccessProbeWorld
    {
        [HostCapability("probe.bad.both", HostBindingEffect.HostStateRead | HostBindingEffect.HostStateWrite)]
        int Read();
    }

    private sealed class BothAccessProbeWorld : IBothAccessProbeWorld
    {
        public int Read() => 1;
    }

    private interface IMissingAllocProbeWorld
    {
        [HostCapability("probe.bad.missing-alloc", HostBindingEffect.HostStateRead)]
        string ReadName();
    }

    private sealed class MissingAllocProbeWorld : IMissingAllocProbeWorld
    {
        public string ReadName() => "name";
    }

    private interface IExtraAllocProbeWorld
    {
        [HostCapability("probe.bad.extra-alloc", HostBindingEffect.HostStateRead | HostBindingEffect.Allocates)]
        int Read();
    }

    private sealed class ExtraAllocProbeWorld : IExtraAllocProbeWorld
    {
        public int Read() => 1;
    }
}
