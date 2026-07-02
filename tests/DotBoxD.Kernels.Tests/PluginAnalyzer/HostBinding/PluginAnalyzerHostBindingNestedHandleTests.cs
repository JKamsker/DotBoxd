using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding
{
    public sealed class PluginAnalyzerHostBindingNestedHandleTests
    {
        private const string NestedHandleSource = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Kernels;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [RpcService]
        public interface IMonsterHandle
        {
            [HostBinding("probe.read.monster.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetThreat();
        }

        [RpcService]
        public interface IProbeWorld
        {
            [HostBinding("probe.read.monster", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            IMonsterHandle GetMonster(string id);
        }

        public sealed record ProbeEvent(string TargetId, int Threshold);

        [Plugin("nested-host-binding-handle")]
        public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetMonster(e.TargetId).GetThreat() >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, "matched");
        }
        """;

        [Fact]
        public async Task Host_binding_call_on_a_returned_service_handle_round_trips_through_AddBindingsFrom()
        {
            var package = PluginAnalyzerGeneratedPackageFactory.Create(
                NestedHandleSource,
                "Sample.ProbePluginPackage");

            using var server = PluginServer.Create(
                configureHost: builder => builder.AddBindingsFrom<Sample.IProbeWorld>(new Sample.ProbeWorld()),
                defaultPolicy: ProbeReadPolicy());

            var kernel = await server.InstallAsync(package);
            var adapter = new ProbeEventAdapter();

            Assert.True(await kernel.ShouldHandleAsync(adapter, new ProbeEvent("monster-42", 40)));
        }

        private static SandboxPolicy ProbeReadPolicy()
            => SandboxPolicyBuilder.Create()
                .GrantLogging()
                .GrantHostMessageWrite()
                .Grant("probe.read.*", new { }, SandboxEffect.HostStateRead)
                .WithFuel(100_000)
                .WithMaxHostCalls(1_000)
                .WithWallTime(TimeSpan.FromSeconds(10))
                .Build();

        private sealed record ProbeEvent(string TargetId, int Threshold);

        private sealed class ProbeEventAdapter : IPluginEventAdapter<ProbeEvent>
        {
            public string EventName => "ProbeEvent";

            public IReadOnlyList<Parameter> Parameters { get; } =
            [
                new("e_TargetId", SandboxType.String),
            new("e_Threshold", SandboxType.I32)
            ];

            public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeEvent e)
                =>
                [
                    SandboxValue.FromString(e.TargetId),
                SandboxValue.FromInt32(e.Threshold)
                ];
        }
    }
}

namespace Sample
{
    using DotBoxD.Abstractions;

    [RpcService]
    public interface IMonsterHandle
    {
        [HostBinding("probe.read.monster.threat", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int GetThreat();
    }

    [RpcService]
    public interface IProbeWorld
    {
        [HostBinding("probe.read.monster", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        IMonsterHandle GetMonster(string id);
    }

    public sealed class MonsterHandle(string id) : IMonsterHandle
    {
        public int GetThreat() => id == "monster-42" ? 42 : 0;
    }

    public sealed class ProbeWorld : IProbeWorld
    {
        public IMonsterHandle GetMonster(string id) => new MonsterHandle(id);
    }
}
