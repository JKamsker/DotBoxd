using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

/// <summary>
/// Shared fixtures for the server extension tests: a hand-built <c>KillMonsters(List&lt;int&gt;)</c>
/// batch kernel that builds a <c>List&lt;Record&lt;I32,Bool&gt;&gt;</c>, the matching <c>host.world.kill</c>
/// binding (kills even ids), and the policies that grant or withhold its capability.
/// </summary>
internal static class RpcKernelTestPackages
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly SandboxType RecordType = SandboxType.Record([SandboxType.I32, SandboxType.Bool]);

    public const string KillBindingId = "host.world.kill";
    public const string KillCapability = "game.world.monster.write.kill";

    public static PluginPackage MonsterKiller()
    {
        Expression Var(string name) => new VariableExpression(name, Span);
        var getItem = new CallExpression("list.get", [Var("ids"), Var("i")], null, Span);
        var kill = new CallExpression(KillBindingId, [getItem], null, Span);
        var newRecord = new CallExpression("record.new", [getItem, kill], RecordType, Span);
        var loopBody = new Statement[]
        {
            new AssignmentStatement("results", new CallExpression("list.add", [Var("results"), newRecord], null, Span), Span)
        };
        var body = new Statement[]
        {
            new AssignmentStatement("results", new CallExpression("list.empty", [], RecordType, Span), Span),
            new ForRangeStatement(
                "i",
                new LiteralExpression(SandboxValue.FromInt32(0), Span),
                new CallExpression("list.count", [Var("ids")], null, Span),
                loopBody,
                Span),
            new ReturnStatement(Var("results"), Span)
        };

        var function = new SandboxFunction(
            "KillMonsters",
            IsEntrypoint: true,
            [new Parameter("ids", SandboxType.List(SandboxType.I32))],
            SandboxType.List(RecordType),
            body);
        var module = new SandboxModule(
            "monster-killer",
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string> { ["pluginId"] = "monster-killer", ["kernel"] = "MonsterKillerKernel" });
        var manifest = new PluginManifest(
            "monster-killer",
            "IMonsterKillerService",
            ExecutionMode.Auto,
            ["Cpu", "Alloc", "HostStateWrite"],
            [],
            [])
        {
            RequiredCapabilities = [KillCapability],
            RpcEntrypoint = "KillMonsters"
        };
        return PluginPackage.Create(manifest, module, new KernelEntrypoints("KillMonsters", "KillMonsters"));
    }

    public static void AddKillBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            KillBindingId,
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite,
            KillCapability,
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.SideEffectingExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var monsterId = ((I32Value)args[0]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: KillBindingId,
                    CapabilityId: KillCapability,
                    Effect: SandboxEffect.HostStateWrite,
                    ResourceId: $"monster:{monsterId}",
                    Fields: context.BindingAuditFields("world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(monsterId % 2 == 0));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    public static SandboxPolicy KillPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite)
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    public static SandboxPolicy NoKillPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .Build();
}
