using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

internal static class PluginAnalyzerHostBindingTestSupport
{
    internal const string GuardianReplicaSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;
        using System.ComponentModel.DataAnnotations;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);
        }

        public sealed record AggroEvent(
            string MonsterId, string PlayerId, int Distance, int MonsterLevel, int PlayerLevel);

        [Plugin("guardian-replica")]
        public sealed partial class GuardianReplicaKernel : IEventKernel<AggroEvent>
        {
            [LiveSetting] [Range(0, 100)] public int LevelGap { get; set; } = 3;
            [LiveSetting] [Range(0, 100)] public int AggroRange { get; set; } = 5;
            [LiveSetting] [Range(0, 100)] public int ProtectMaxLevel { get; set; } = 5;
            [LiveSetting] public string CalmStrength { get; set; } = "20";

            public bool ShouldHandle(AggroEvent e, HookContext ctx)
                => e.MonsterLevel - e.PlayerLevel >= LevelGap &&
                   e.Distance <= AggroRange &&
                   e.PlayerLevel <= ProtectMaxLevel &&
                   ctx.Host<IProbeWorld>().GetValue(e.MonsterId) > 0;

            public void Handle(AggroEvent e, HookContext ctx)
                => ctx.Messages.Send(e.MonsterId, "calm:" + e.PlayerId + ":" + CalmStrength);
        }
        """;

    internal static void AddProbeBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(ProbeReadBinding("host.probe.getValue", "probe.read.value", 42));
        builder.AddBinding(ProbeReadBinding("host.probe.getSecret", "probe.admin.secret", 7));
    }

    internal static SandboxPolicy ProbeReadPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .Grant("probe.read.*", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    internal static SandboxPolicy MessageWriteOnlyPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static BindingDescriptor ProbeReadBinding(string id, string capability, int value)
        => new(
            id,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            capability,
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var entityId = ((StringValue)args[0]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: id,
                    CapabilityId: capability,
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"entity:{entityId}",
                    Fields: context.BindingAuditFields("probe", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(value));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    internal sealed record ProbeEvent(string TargetId, string Message, int Threshold);

    internal sealed class ProbeEventAdapter : IPluginEventAdapter<ProbeEvent>
    {
        public string EventName => "ProbeEvent";

        public IReadOnlyList<Parameter> Parameters { get; } =
        [
            new("e_TargetId", SandboxType.String),
            new("e_Message", SandboxType.String),
            new("e_Threshold", SandboxType.I32)
        ];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeEvent e)
            =>
            [
                SandboxValue.FromString(e.TargetId),
                SandboxValue.FromString(e.Message),
                SandboxValue.FromInt32(e.Threshold)
            ];
    }
}
