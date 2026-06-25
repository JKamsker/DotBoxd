namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

internal static class PluginAnalyzerKernelMethodTestSources
{
    public const string InlinedGate = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record AggroEvent(
            string MonsterId, string Message, int MonsterLevel, int PlayerLevel, int Distance);

        [Plugin("inlined-gate")]
        public sealed partial class InlinedGateKernel : IEventKernel<AggroEvent>
        {
            public bool ShouldHandle(AggroEvent e, HookContext ctx)
                => IsBullying(e.MonsterLevel, e.PlayerLevel) && IsClose(e.Distance);

            public void Handle(AggroEvent e, HookContext ctx)
                => ctx.Messages.Send(e.MonsterId, e.Message);

            [KernelMethod]
            public static bool IsBullying(int monsterLevel, int playerLevel) => monsterLevel - playerLevel >= 3;

            [KernelMethod]
            public static bool IsClose(int distance) => distance <= 5;
        }
        """;

    public const string InlinedHostBinding = """
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

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("inlined-host-binding")]
        public sealed partial class InlinedHostBindingKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => IsAtLeast(ctx.Host<IProbeWorld>().GetValue(e.TargetId), e.Threshold);

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);

            [KernelMethod]
            public static bool IsAtLeast(int value, int threshold) => value >= threshold;
        }
        """;

    public const string Chain = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Where((e, ctx) => IsBullyingAndClose(e.MonsterLevel, e.PlayerLevel, e.Distance))
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

            [KernelMethod]
            public static bool IsBullyingAndClose(int monsterLevel, int playerLevel, int distance)
                => monsterLevel - playerLevel >= 3 && distance <= 5;
        }
        """;

    public const string MultiStatement = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Where((e, ctx) => Unsupported(e.Distance))
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "calm"));

            // A multi-statement body is not inlineable, so the whole chain fails safe with no package.
            [KernelMethod]
            public static bool Unsupported(int distance)
            {
                var doubled = distance * 2;
                return doubled <= 10;
            }
        }
        """;
}
