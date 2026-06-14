using DotBoxd.Kernels;
using DotBoxd.Hosting;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>
/// End-to-end coverage of host-binding-call lowering: a kernel reaching a host service through
/// <c>ctx.Host&lt;T&gt;()</c> whose method carries <c>[HostBinding(id, capability)]</c> lowers to a
/// sandbox <c>CallExpression(id, …)</c>, the capability is derived into the manifest, and the install
/// is gated on the policy granting that capability (wildcard-aware, fail-closed). Also covers a
/// <c>[Capability]</c>-gated event-property read contributing its capability.
/// </summary>
public sealed class PluginAnalyzerHostBindingTests
{
    private const string ProbeSource = """
        using DotBoxd.Kernels;
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getValue", "probe.read.value", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetValue(string id);

            [HostBinding("host.probe.getSecret", "probe.admin.secret", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetSecret(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("host-binding-probe")]
        public sealed partial class ProbeKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetValue(e.TargetId) >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    private const string SecretSource = """
        using DotBoxd.Kernels;
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

        namespace Sample;

        public interface IProbeWorld
        {
            [HostBinding("host.probe.getSecret", "probe.admin.secret", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            int GetSecret(string id);
        }

        public sealed record ProbeEvent(string TargetId, string Message, int Threshold);

        [Plugin("host-binding-secret")]
        public sealed partial class SecretKernel : IEventKernel<ProbeEvent>
        {
            public bool ShouldHandle(ProbeEvent e, HookContext ctx)
                => ctx.Host<IProbeWorld>().GetSecret(e.TargetId) >= e.Threshold;

            public void Handle(ProbeEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    private const string GatedPropertySource = """
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

        namespace Sample;

        public sealed record GatedEvent(
            string TargetId,
            string Message,
            [property: Capability("event.read.health")] int Health);

        [Plugin("gated-property")]
        public sealed partial class GatedKernel : IEventKernel<GatedEvent>
        {
            public bool ShouldHandle(GatedEvent e, HookContext ctx) => e.Health > 0;

            public void Handle(GatedEvent e, HookContext ctx)
                => ctx.Messages.Send(e.TargetId, e.Message);
        }
        """;

    [Fact]
    public void Host_binding_call_derives_its_capability_into_the_manifest()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");

        Assert.Contains("probe.read.value", package.Manifest.RequiredCapabilities);
        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);
        // GetSecret is declared on the interface but never called, so its capability is not required.
        Assert.DoesNotContain("probe.admin.secret", package.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task Host_binding_kernel_installs_and_runs_under_a_wildcard_grant()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");
        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(
            messages,
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallAsync(package);
        Assert.Equal("host-binding-probe", kernel.Manifest.PluginId);

        var adapter = new ProbeEventAdapter();
        // The host.probe.getValue binding returns 42.
        Assert.True(await kernel.ShouldHandleAsync(adapter, new ProbeEvent("player-1", "hi", 10)));
        Assert.False(await kernel.ShouldHandleAsync(adapter, new ProbeEvent("player-1", "hi", 50)));
    }

    [Fact]
    public async Task Host_binding_kernel_installs_through_json_export_import()
    {
        // Mirrors the GameServer example path: the plugin exports verified IR to JSON and the server
        // imports + installs it, exercising host-binding CallExpression + requiredCapabilities round-trip.
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");
        var json = PluginPackageJsonSerializer.Export(package, indented: true);

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(
            messages,
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallJsonAsync(json);
        Assert.Equal("host-binding-probe", kernel.Manifest.PluginId);
        Assert.Contains("probe.read.value", kernel.Manifest.RequiredCapabilities);
    }

    [Fact]
    public async Task Host_binding_kernel_is_denied_when_its_capability_is_not_granted()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(ProbeSource, "Sample.ProbePluginPackage");
        using var server = PluginServer.Create(
            configureHost: AddProbeBindings,
            defaultPolicy: MessageWriteOnlyPolicy());

        // probe.read.value is never granted → preparation fails closed.
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await server.InstallAsync(package).AsTask());
    }

    [Fact]
    public async Task Wildcard_grant_does_not_cross_capability_subtrees()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(SecretSource, "Sample.SecretPluginPackage");
        using var server = PluginServer.Create(
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        // The probe.read.* grant covers probe.read.value but not probe.admin.secret.
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await server.InstallAsync(package).AsTask());
    }

    private const string GuardianReplicaSource = """
        using DotBoxd.Kernels;
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;
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

    [Fact]
    public async Task Guardian_shaped_kernel_with_live_settings_and_host_binding_installs_via_json()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            GuardianReplicaSource, "Sample.GuardianReplicaPluginPackage");
        var json = PluginPackageJsonSerializer.Export(package, indented: true);

        var messages = new InMemoryPluginMessageSink();
        using var server = PluginServer.Create(
            messages,
            configureHost: AddProbeBindings,
            defaultPolicy: ProbeReadPolicy());

        var kernel = await server.InstallJsonAsync(json);
        Assert.Equal("guardian-replica", kernel.Manifest.PluginId);
    }

    [Fact]
    public void Gated_event_property_read_derives_its_capability()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(GatedPropertySource, "Sample.GatedPluginPackage");

        Assert.Contains("event.read.health", package.Manifest.RequiredCapabilities);
        Assert.Contains("host.message.write", package.Manifest.RequiredCapabilities);
    }

    private static void AddProbeBindings(SandboxHostBuilder builder)
    {
        builder.AddBinding(ProbeReadBinding("host.probe.getValue", "probe.read.value", 42));
        builder.AddBinding(ProbeReadBinding("host.probe.getSecret", "probe.admin.secret", 7));
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

    private static SandboxPolicy MessageWriteOnlyPolicy()
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
            CompiledBinding.RuntimeStub("DotBoxd.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { });

    private sealed record ProbeEvent(string TargetId, string Message, int Threshold);

    private sealed class ProbeEventAdapter : IPluginEventAdapter<ProbeEvent>
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
