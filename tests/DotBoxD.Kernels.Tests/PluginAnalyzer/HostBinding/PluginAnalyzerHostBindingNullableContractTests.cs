using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class PluginAnalyzerHostBindingNullableContractTests
{
    private const string NullableReturnHostBindingSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int? GetValue(string id);
        }

        public sealed record ProbeEvent(string TargetId);

        [Plugin("nullable-host-binding-return")]
        public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetValue(e.TargetId) is not null;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, "message");
        }
        """;

    private const string NullableDtoHostBindingSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record ProbeQuery(int? Value);

        public interface IProbeWorld
        {
            [HostBinding("host.probe.echo", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int Echo(ProbeQuery query);
        }

        public sealed record ProbeEvent(int? Value, string TargetId);

        [Plugin("nullable-host-binding-dto")]
        public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().Echo(new ProbeQuery(e.Value)) > 0;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, "message");
        }
        """;

    [Fact]
    public void Generator_rejects_nullable_host_binding_return_types()
        => AssertHostBindingSourceRejected(NullableReturnHostBindingSource);

    [Fact]
    public void Generator_rejects_nullable_host_binding_dto_fields()
        => AssertHostBindingSourceRejected(NullableDtoHostBindingSource);

    private static void AssertHostBindingSourceRejected(string source)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("must not contain nullable value types", StringComparison.Ordinal));
    }
}
