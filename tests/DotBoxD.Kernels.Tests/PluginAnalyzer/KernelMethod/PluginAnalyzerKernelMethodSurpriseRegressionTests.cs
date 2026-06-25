using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodSurpriseRegressionTests
{
    [Fact]
    public void KernelMethod_rejects_repeated_non_repeatable_host_binding_argument()
    {
        var source = PluginAnalyzerKernelMethodTestSources.InlinedHostBinding.Replace(
            "value >= threshold",
            "value + value >= threshold",
            StringComparison.Ordinal);

        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("used more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelMethod_argument_reuse_counts_parameter_symbols_not_matching_member_names()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
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
            }

            public sealed record ProbeEvent(string TargetId, string Message, int Level);

            [Plugin("member-name-kernel-method")]
            public sealed partial class MemberNameKernel : IEventKernel<ProbeEvent>
            {
                public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                    => Matches(e, ctx.Host<IProbeWorld>().GetValue(e.TargetId));

                public void Handle(ProbeEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);

                [KernelMethod]
                public static bool Matches(ProbeEvent e, int Level) => e.Level == Level;
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DBXK100");
    }

    [Fact]
    public void Rpc_KernelMethod_nullable_parameter_reports_direct_diagnostic()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("nullable-kernel-method")]
            public sealed partial class NullableKernelMethodKernel
            {
                public int Run(int value, HookContext ctx)
                {
                    return OrZero(value);
                }

                [KernelMethod]
                public static int OrZero(int? value) => value ?? 0;
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("nullable parameter", StringComparison.Ordinal));
    }
}
