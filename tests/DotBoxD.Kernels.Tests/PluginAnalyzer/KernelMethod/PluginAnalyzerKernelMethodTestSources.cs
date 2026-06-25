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

    public const string RichRecordHelperChain = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        public sealed record ThreatSnapshot(string MonsterId, int Distance);

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Select(e => new ThreatSnapshot(e.MonsterId, e.Distance))
                    .Where(snapshot => IsClose(snapshot))
                    .Run((snapshot, ctx) => ctx.Messages.Send(snapshot.MonsterId, "calm"));

            [KernelMethod]
            public static bool IsClose(ThreatSnapshot snapshot) => snapshot.Distance <= 5;
        }
        """;

    public const string ProjectedRecordHelperUsesProjectedValueChain = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        public sealed record ThreatSnapshot(string MonsterId, int Distance);

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Select(e => new ThreatSnapshot(e.MonsterId, e.Distance + 10))
                    .Where(snapshot => IsClose(snapshot))
                    .Run((snapshot, ctx) => ctx.Messages.Send(snapshot.MonsterId, "calm"));

            [KernelMethod]
            public static bool IsClose(ThreatSnapshot snapshot) => snapshot.Distance <= 5;
        }
        """;

    public const string RunSendHelperChain = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .Run((e, ctx) => SendCalm(ctx, e.MonsterId));

            [KernelMethod]
            public static void SendCalm(HookContext ctx, string monsterId)
                => ctx.Messages.Send(monsterId, "calm");
        }
        """;

    public const string ExtensionNamedDefaultChain = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Where(e => e.IsBullyingAndClose(playerLevel: e.PlayerLevel, monsterLevel: e.MonsterLevel))
                    .Run((e, ctx) => SendCalm(monsterId: e.MonsterId, ctx: ctx));

            [KernelMethod]
            public static bool IsBullyingAndClose(
                this global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent e,
                int monsterLevel,
                int playerLevel,
                int maxDistance = 5)
                => monsterLevel - playerLevel >= 3 && e.Distance <= maxDistance;

            [KernelMethod]
            public static void SendCalm(HookContext ctx, string monsterId, string message = "calm")
                => ctx.Messages.Send(monsterId, message);
        }
        """;

    public const string NullableDefaultChain = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace ChainSample;

        public static class Usage
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod.KernelMethodAggroEvent>()
                    .Where(e => IsClose(e.Distance))
                    .Run((e, ctx) => ctx.Messages.Send(e.MonsterId, "nullable"));

            [KernelMethod]
            public static bool IsClose(int distance, int? maxDistance = 5)
                => distance <= 5;
        }
        """;

    public const string HandleSendHelper = """
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;

        namespace Sample;

        public sealed record AggroEvent(
            string MonsterId, string Message, int MonsterLevel, int PlayerLevel, int Distance);

        [Plugin("inlined-handle-helper")]
        public sealed partial class InlinedHandleHelperKernel : IEventKernel<AggroEvent>
        {
            public bool ShouldHandle(AggroEvent e, HookContext ctx) => e.Distance <= 5;

            public void Handle(AggroEvent e, HookContext ctx)
                => SendCalm(ctx, e.MonsterId);

            [KernelMethod]
            public static void SendCalm(HookContext ctx, string monsterId)
                => ctx.Messages.Send(monsterId, "calm");
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
