using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis.CSharp;

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
    public void KernelMethod_rejects_repeated_non_repeatable_host_binding_property_argument()
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
                [HostBinding("host.probe.value", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                int Value { get; }
            }

            public sealed record ProbeEvent(int Threshold, string TargetId, string Message);

            [Plugin("host-property-kernel-method")]
            public sealed partial class HostPropertyKernel : IEventKernel<ProbeEvent>
            {
                public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                    => Matches(ctx.Host<IProbeWorld>().Value, e.Threshold);

                public void Handle(ProbeEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);

                [KernelMethod]
                public static bool Matches(int value, int threshold) => value + value >= threshold;
            }
            """);

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

    [Fact]
    public void KernelMethod_send_helper_rejects_repeated_non_repeatable_argument()
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
                [HostBinding("host.probe.getTarget", "probe.read.target", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                string GetTarget(string id);
            }

            public sealed record DamageEvent(string TargetId);

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                {
                    hooks.On<DamageEvent>()
                        .Run((e, ctx) => SendBoth(ctx, ctx.Host<IProbeWorld>().GetTarget(e.TargetId)));
                }

                [KernelMethod]
                public static void SendBoth(HookContext ctx, string value)
                    => ctx.Messages.Send(value, value);
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("used more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelMethod_reuse_validator_treats_typed_default_as_repeatable()
    {
        var expression = SyntaxFactory.ParseExpression("default(int)");

        Assert.True(KernelMethodArgumentReuseValidator.IsRepeatableArgument(expression));
    }

    [Fact]
    public void KernelMethod_reuse_validator_ignores_nameof_parameter_mentions()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
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

            public sealed record ProbeEvent(string TargetId);

            [Plugin("nameof-kernel-method")]
            public sealed partial class NameofKernel : IEventKernel<ProbeEvent>
            {
                public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                    => Matches(ctx.Host<IProbeWorld>().GetValue(e.TargetId));

                public void Handle(ProbeEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "ok");

                [KernelMethod]
                public static bool Matches(int value) => value >= 0 && nameof(value) == "value";
            }
            """);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("used more than once", StringComparison.Ordinal));
    }
}
