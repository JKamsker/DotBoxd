using SafeIR;
using SafeIR.Hosting;
using SafeIR.Plugins;

namespace SafeIR.Tests;

/// <summary>
/// End-to-end proof of the kernel RPC service authoring path (Followup #2.3): a plain-C# batch class
/// with <c>[KernelRpcService]</c> — locals, a <c>foreach</c> over a list, a host binding per element, and
/// a <c>List&lt;KillResult&gt;</c> of DTOs built and returned — is lowered by the generator to verified
/// IR, installed via <see cref="PluginServer.InstallRpcAsync"/>, and invoked request/response returning
/// the list of objects in one roundtrip.
/// </summary>
public sealed class RpcKernelGenerationTests
{
    private const string MonsterKillerSource = """
        using System.Collections.Generic;
        using SafeIR;
        using SafeIR.Plugins;
        using SafeIR.Server.Abstractions;

        namespace Sample;

        public interface IGameWorld
        {
            [HostBinding("host.world.kill", "game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            bool Kill(int id);
        }

        public readonly record struct KillResult(int MonsterId, bool Success);

        [KernelRpcService("monster-killer")]
        public sealed partial class MonsterKillerKernel
        {
            public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<KillResult>();
                foreach (var id in monsterIds)
                {
                    var ok = ctx.Host<IGameWorld>().Kill(id);
                    results.Add(new KillResult(id, ok));
                }

                return results;
            }
        }
        """;

    [Fact]
    public async Task A_generated_batch_kernel_installs_and_returns_a_list_of_dtos_in_one_roundtrip()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MonsterKillerSource, "Sample.MonsterKillerPluginPackage");
        Assert.Equal("KillMonsters", package.Manifest.RpcEntrypoint);
        Assert.Contains("game.world.monster.write.kill", package.Manifest.RequiredCapabilities);

        using var server = PluginServer.Create(configureHost: AddKillBinding, defaultPolicy: KillPolicy());
        var kernel = await server.InstallRpcAsync(package);

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(10), SandboxValue.FromInt32(11), SandboxValue.FromInt32(12)],
            SandboxType.I32);

        var result = await kernel.InvokeRpcAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(3, list.Values.Count);
        AssertKill(list.Values[0], 10, true);
        AssertKill(list.Values[1], 11, false);
        AssertKill(list.Values[2], 12, true);
    }

    private static void AssertKill(SandboxValue value, int expectedId, bool expectedSuccess)
    {
        var record = Assert.IsType<RecordValue>(value);
        Assert.Equal([SandboxValue.FromInt32(expectedId), SandboxValue.FromBool(expectedSuccess)], record.Fields);
    }

    private static void AddKillBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            "host.world.kill",
            SemVersion.One,
            [SandboxType.I32],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite,
            "game.world.monster.write.kill",
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
                    BindingId: "host.world.kill",
                    CapabilityId: "game.world.monster.write.kill",
                    Effect: SandboxEffect.HostStateWrite,
                    ResourceId: $"monster:{monsterId}",
                    Fields: context.BindingAuditFields("world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(monsterId % 2 == 0));
            },
            CompiledBinding.RuntimeStub("SafeIR.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy KillPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite)
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
