using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelMixedArgumentGenerationTests
{
    private const string MixedHostBindingSource = """
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

        [ServerExtension("mixed-host-binding")]
        public sealed partial class MixedHostBindingKernel
        {
            public List<FightResult> Check(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<FightResult>();
                foreach (var id in monsterIds)
                {
                    var ok = ctx.Host<IGameWorld>().CanFight(monsterId: id, 10);
                    results.Add(new FightResult(id, ok));
                }

                return results;
            }
        }
        """;

    private const string MixedRecordConstructorSource = """
        using System.Collections.Generic;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace Sample;

        public readonly record struct FightResult(int MonsterId, bool Success);

        [ServerExtension("mixed-record-constructor")]
        public sealed partial class MixedRecordConstructorKernel
        {
            public List<FightResult> Build(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<FightResult>();
                foreach (var id in monsterIds)
                {
                    results.Add(new FightResult(MonsterId: id, id >= 10));
                }

                return results;
            }
        }
        """;

    [Fact]
    public async Task Host_binding_mixed_arguments_are_lowered_in_parameter_order()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            MixedHostBindingSource,
            "Sample.MixedHostBindingPluginPackage");

        using var server = PluginServer.Create(configureHost: AddThresholdBinding, defaultPolicy: ThresholdPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(9), SandboxValue.FromInt32(10)],
            SandboxType.I32);

        var result = await kernel.InvokeServerExtensionAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(2, list.Values.Count);
        AssertFight(list.Values[0], 9, false);
        AssertFight(list.Values[1], 10, true);
    }

    [Fact]
    public async Task Record_constructor_mixed_arguments_are_lowered_in_field_order()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            MixedRecordConstructorSource,
            "Sample.MixedRecordConstructorPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: CpuPolicy().Build());
        var kernel = await server.InstallServerExtensionAsync(package);

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(9), SandboxValue.FromInt32(10)],
            SandboxType.I32);

        var result = await kernel.InvokeServerExtensionAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(2, list.Values.Count);
        AssertFight(list.Values[0], 9, false);
        AssertFight(list.Values[1], 10, true);
    }

    [Theory]
    [InlineData("ref int value", "ref mutable")]
    [InlineData("in int value", "in mutable")]
    [InlineData("out int value", "out mutable")]
    public void Host_binding_ref_kind_arguments_are_rejected(string parameter, string argument)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics($$"""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IGameWorld
            {
                [HostBinding("host.world.canFight", "game.world.monster.read.threshold", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                bool CanFight({{parameter}});
            }

            [ServerExtension("ref-host-binding")]
            public sealed partial class RefHostBindingKernel
            {
                public List<int> Check(List<int> monsterIds, HookContext ctx)
                {
                    var results = new List<int>();
                    foreach (var id in monsterIds)
                    {
                        var mutable = id;
                        var ok = ctx.Host<IGameWorld>().CanFight({{argument}});
                        if (ok)
                        {
                            results.Add(mutable);
                        }
                    }

                    return results;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot use ref, in, or out", StringComparison.Ordinal));
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
