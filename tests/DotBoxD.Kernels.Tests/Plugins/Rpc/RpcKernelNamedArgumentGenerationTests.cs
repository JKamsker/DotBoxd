using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelNamedArgumentGenerationTests
{
    private const string NamedHostBindingSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public interface IGameWorld
        {
            [HostBinding("host.world.canFight", "game.world.monster.read.threshold", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            bool CanFight(int monsterId, int threshold);
        }

        public readonly record struct FightResult(int MonsterId, bool Success);

        [KernelRpcService("named-host-binding")]
        public sealed partial class NamedHostBindingKernel
        {
            public List<FightResult> Check(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<FightResult>();
                foreach (var id in monsterIds)
                {
                    var ok = ctx.Host<IGameWorld>().CanFight(threshold: 10, monsterId: id);
                    results.Add(new FightResult(id, ok));
                }

                return results;
            }
        }
        """;

    private const string NamedRecordConstructorSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public readonly record struct FightResult(int MonsterId, bool Success);

        [KernelRpcService("named-record-constructor")]
        public sealed partial class NamedRecordConstructorKernel
        {
            public List<FightResult> Build(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<FightResult>();
                foreach (var id in monsterIds)
                {
                    results.Add(new FightResult(Success: id >= 10, MonsterId: id));
                }

                return results;
            }
        }
        """;

    [Fact]
    public async Task Host_binding_named_arguments_are_lowered_in_parameter_order()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            NamedHostBindingSource,
            "Sample.NamedHostBindingPluginPackage");

        using var server = PluginServer.Create(configureHost: AddThresholdBinding, defaultPolicy: ThresholdPolicy());
        var kernel = await server.InstallRpcAsync(package);

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(9), SandboxValue.FromInt32(10), SandboxValue.FromInt32(11)],
            SandboxType.I32);

        var result = await kernel.InvokeRpcAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(3, list.Values.Count);
        AssertFight(list.Values[0], 9, false);
        AssertFight(list.Values[1], 10, true);
        AssertFight(list.Values[2], 11, true);
    }

    [Fact]
    public async Task Record_constructor_named_arguments_are_lowered_in_parameter_order()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            NamedRecordConstructorSource,
            "Sample.NamedRecordConstructorPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: CpuPolicy().Build());
        var kernel = await server.InstallRpcAsync(package);

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(9), SandboxValue.FromInt32(10)],
            SandboxType.I32);

        var result = await kernel.InvokeRpcAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(2, list.Values.Count);
        AssertFight(list.Values[0], 9, false);
        AssertFight(list.Values[1], 10, true);
    }

    private static void AssertFight(SandboxValue value, int expectedId, bool expectedSuccess)
    {
        var record = Assert.IsType<RecordValue>(value);
        Assert.Equal([SandboxValue.FromInt32(expectedId), SandboxValue.FromBool(expectedSuccess)], record.Fields);
    }

    private static void AddThresholdBinding(SandboxHostBuilder builder)
        => builder.AddBinding(new BindingDescriptor(
            "host.world.canFight",
            SemVersion.One,
            [SandboxType.I32, SandboxType.I32],
            SandboxType.Bool,
            SandboxEffect.Cpu | SandboxEffect.HostStateRead,
            "game.world.monster.read.threshold",
            BindingCostModel.Fixed(2),
            AuditLevel.PerResource,
            BindingSafety.ReadOnlyExternal,
            (context, args, _) =>
            {
                var startedAt = DateTimeOffset.UtcNow;
                var monsterId = ((I32Value)args[0]).Value;
                var threshold = ((I32Value)args[1]).Value;
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "BindingCall",
                    startedAt,
                    true,
                    BindingId: "host.world.canFight",
                    CapabilityId: "game.world.monster.read.threshold",
                    Effect: SandboxEffect.HostStateRead,
                    ResourceId: $"monster:{monsterId}",
                    Fields: context.BindingAuditFields("world", startedAt)));
                return ValueTask.FromResult(SandboxValue.FromBool(monsterId >= threshold));
            },
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy ThresholdPolicy()
        => CpuPolicy()
            .Grant("game.world.monster.read.*", new { }, SandboxEffect.HostStateRead)
            .Build();

    private static SandboxPolicyBuilder CpuPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10));
}
