using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed class PluginAnalyzerKernelMethodImplicitThisRegressionTests
{
    [Fact]
    public void Context_KernelMethod_implicit_this_property_lowers_to_host_binding()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record DamageEvent(string TargetId);

            public sealed class GameContext
            {
                public static GameContext FromHookContext(HookContext ctx)
                    => throw new System.NotSupportedException();

                public IPluginMessageSink Messages
                    => throw new System.NotSupportedException();

                [HostBinding("host.ctx.label", "ctx.read.label", SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
                public string Label => throw new System.NotSupportedException();

                [KernelMethod]
                public string Tag(string suffix) => Label + ":" + suffix;
            }

            public static class Usage
            {
                public static void Configure(RemoteHookRegistry hooks)
                    => hooks.On<DamageEvent, GameContext>(GameContext.FromHookContext)
                        .Run((e, ctx) => ctx.Messages.Send(e.TargetId, ctx.Tag("arg")));
            }
            """);
        var generated = string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));

        Assert.Contains("host.ctx.label", generated, StringComparison.Ordinal);
        Assert.Contains("ctx.read.label", generated, StringComparison.Ordinal);
    }
}
