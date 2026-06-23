using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using KernelSemVersion = DotBoxD.Kernels.Model.SemVersion;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// End-to-end guard for issue #67: the inline scoped-handle form
/// <c>_world.Monsters.Get(id).GetThreat()</c> must install and invoke identically to the local-variable form.
/// Before the fix the inline lowering dropped the captured scope, so the emitted host call had arity 0 while
/// the registered binding expects 1 (the captured id) — the package built clean but threw
/// <c>SandboxValidationException</c> (<c>E-CALL-ARITY</c>) at install. A synchronous scoped method is used so
/// the test exercises only the arity defect, independent of async/runtime-async gating.
/// </summary>
public sealed class ServerExtensionInlineScopedHandleRuntimeTests
{
    private const string ThreatBindingId = "host.Sample.IMonster.GetThreat";
    private const string ThreatCapability = "game.world.monster.read.threat";
    private const int ThreatValue = 7;

    private const string InlineKernel = """
        using DotBoxD.Abstractions;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [DotBoxDService]
        public interface IGameWorldAccess
        {
            IMonsterControl Monsters { get; }
        }

        [DotBoxDService]
        public interface IMonsterControl
        {
            [HostCapability("game.world.monster.read.handle")]
            IMonster Get(string entityId);
        }

        [DotBoxDService]
        public interface IMonster
        {
            [HostCapability("game.world.monster.read.threat")]
            int GetThreat();
        }

        [ServerExtension(typeof(IMonsterControl))]
        public sealed partial class ScopedReadKernel
        {
            private readonly IGameWorldAccess _world;
            public ScopedReadKernel(IGameWorldAccess world) => _world = world;

            [ServerExtensionMethod]
            public int ReadThreat(string id, HookContext ctx)
            {
                return _world.Monsters.Get(id).GetThreat();   // inline scoped receiver
            }
        }
        """;

    private const string LocalKernel = """
        using DotBoxD.Abstractions;
        using DotBoxD.Services.Attributes;

        namespace Sample;

        [DotBoxDService]
        public interface IGameWorldAccess
        {
            IMonsterControl Monsters { get; }
        }

        [DotBoxDService]
        public interface IMonsterControl
        {
            [HostCapability("game.world.monster.read.handle")]
            IMonster Get(string entityId);
        }

        [DotBoxDService]
        public interface IMonster
        {
            [HostCapability("game.world.monster.read.threat")]
            int GetThreat();
        }

        [ServerExtension(typeof(IMonsterControl))]
        public sealed partial class ScopedReadKernel
        {
            private readonly IGameWorldAccess _world;
            public ScopedReadKernel(IGameWorldAccess world) => _world = world;

            [ServerExtensionMethod]
            public int ReadThreat(string id, HookContext ctx)
            {
                var monster = _world.Monsters.Get(id);        // scoped handle bound to a local first
                return monster.GetThreat();
            }
        }
        """;

    [Fact]
    public async Task Inline_scoped_handle_call_installs_and_invokes_like_the_local_form()
    {
        var local = await RunAsync(LocalKernel);
        var inline = await RunAsync(InlineKernel);   // threw E-CALL-ARITY at install before the fix

        Assert.Equal(ThreatValue, local);
        Assert.Equal(local, inline);
    }

    private static async Task<int> RunAsync(string source)
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            source,
            "Sample.ScopedReadPluginPackage",
            typeof(DotBoxD.Services.Attributes.DotBoxDServiceAttribute));

        using var server = PluginServer.Create(
            configureHost: AddThreatBinding,
            defaultPolicy: ThreatReadPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([SandboxValue.FromString("monster-1")]);
        return Assert.IsType<I32Value>(result).Value;
    }

    private static void AddThreatBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            ThreatBindingId,
            KernelSemVersion.One,
            [SandboxType.String],   // the scope id captured by Monsters.Get(id)
            SandboxType.I32,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            ThreatCapability,
            BindingCostModel.Fixed(1),
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
                    BindingId: ThreatBindingId,
                    CapabilityId: ThreatCapability,
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"monster:{entityId}",
                    Fields: context.BindingAuditFields("game-world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromInt32(ThreatValue));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy ThreatReadPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead)
            .WithFuel(100_000)
            .WithMaxHostCalls(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
