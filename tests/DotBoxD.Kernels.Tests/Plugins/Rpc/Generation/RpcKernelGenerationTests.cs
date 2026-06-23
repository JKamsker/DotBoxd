using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
namespace DotBoxD.Kernels.Tests.Plugins.Rpc;
/// <summary>
/// End-to-end proof of the server extension authoring path (Followup #2.3): a plain-C# batch class
/// with <c>[ServerExtension]</c> — locals, a <c>foreach</c> over a list, a host binding per element, and
/// a <c>List&lt;KillResult&gt;</c> of DTOs built and returned — is lowered by the generator to verified
/// IR, installed via <see cref="DotBoxD.Plugins.PluginServer.InstallServerExtensionAsync"/>, and invoked request/response returning
/// the list of objects in one roundtrip.
/// </summary>
public sealed class RpcKernelGenerationTests
{
    private const string MonsterKillerSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;
        using DotBoxD.Abstractions;
        namespace Sample;
        public interface IGameWorld
        {
            [HostBinding("host.world.kill", "game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            bool Kill(int id);
        }
        public readonly record struct KillResult(int MonsterId, bool Success);
        [ServerExtension("monster-killer")]
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
    private const string InjectedMonsterKillerSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;
        namespace Sample;
        public interface IGameWorld
        {
            [HostBinding("host.world.kill", "game.world.monster.write.kill", SandboxEffect.Cpu | SandboxEffect.HostStateWrite)]
            bool Kill(int id);
        }
        public readonly record struct KillResult(int MonsterId, bool Success);
        [ServerExtension("monster-killer")]
        public sealed partial class MonsterKillerKernel
        {
            private readonly IGameWorld _world;
            public MonsterKillerKernel(IGameWorld world) => _world = world;
            public List<KillResult> KillMonsters(List<int> monsterIds, HookContext ctx)
            {
                var results = new List<KillResult>();
                foreach (var id in monsterIds)
                {
                    var ok = _world.Kill(id);
                    results.Add(new KillResult(id, ok));
                }
                return results;
            }
        }
        """;
    private const string AsyncHostBindingSource = """
        using System.Collections.Generic;
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;
        namespace Sample;
        public interface IGameWorld
        {
            [HostBinding("host.world.getLevel", "game.world.monster.read.level", SandboxEffect.Cpu | SandboxEffect.HostStateRead, IsAsync = true)]
            int GetLevel(int id);
        }
        [ServerExtension("async-level")]
        public sealed partial class AsyncLevelKernel
        {
            public int SumLevels(List<int> monsterIds, HookContext ctx)
            {
                var total = 0;
                foreach (var id in monsterIds)
                {
                    total += ctx.Host<IGameWorld>().GetLevel(id);
                }
                return total;
            }
        }
        """;
    private const string ControlStringSource = """
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;
        namespace Sample;
        [ServerExtension("control-string")]
        public sealed partial class ControlStringKernel
        {
            public string Text(HookContext ctx)
            {
                return "\b\f";
            }
        }
        """;
    private const string RecordParameterSource = """
        using DotBoxD.Kernels;
        using DotBoxD.Kernels.Sandbox;
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;
        namespace Sample;
        public readonly record struct WorldPoint(int Position);
        public readonly record struct WorldRangeQuery(WorldPoint Center, int Radius, int MaxResults);
        [ServerExtension("range-query")]
        public sealed partial class RangeQueryKernel
        {
            public int CountInRange(WorldRangeQuery query, HookContext ctx)
            {
                return query.Center.Position + query.Radius + query.MaxResults;
            }
        }
        """;
    [Fact]
    public async Task A_generated_batch_kernel_reads_a_nested_record_parameter_server_side()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(RecordParameterSource, "Sample.RangeQueryPluginPackage");
        Assert.Equal("CountInRange", package.Manifest.RpcEntrypoint);
        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var query = SandboxValue.FromRecord(
        [
            SandboxValue.FromRecord([SandboxValue.FromInt32(5)]),
            SandboxValue.FromInt32(6),
            SandboxValue.FromInt32(7)
        ]);
        var result = await kernel.InvokeServerExtensionAsync([query]);
        Assert.Equal(18, Assert.IsType<I32Value>(result).Value);
    }
    [Fact]
    public async Task A_generated_batch_kernel_installs_and_returns_a_list_of_dtos_in_one_roundtrip()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MonsterKillerSource, "Sample.MonsterKillerPluginPackage");
        Assert.Equal("KillMonsters", package.Manifest.RpcEntrypoint);
        Assert.Contains("game.world.monster.write.kill", package.Manifest.RequiredCapabilities);
        using var server = DotBoxD.Plugins.PluginServer.Create(configureHost: AddKillBinding, defaultPolicy: KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);
        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(10), SandboxValue.FromInt32(11), SandboxValue.FromInt32(12)],
            SandboxType.I32);
        var result = await kernel.InvokeServerExtensionAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(3, list.Values.Count);
        AssertKill(list.Values[0], 10, true);
        AssertKill(list.Values[1], 11, false);
        AssertKill(list.Values[2], 12, true);
    }

    [Fact]
    public async Task A_generated_batch_kernel_can_call_an_injected_host_service()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            InjectedMonsterKillerSource, "Sample.MonsterKillerPluginPackage");
        Assert.Equal("KillMonsters", package.Manifest.RpcEntrypoint);
        Assert.Contains("game.world.monster.write.kill", package.Manifest.RequiredCapabilities);

        using var server = PluginServer.Create(configureHost: AddKillBinding, defaultPolicy: KillPolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var ids = SandboxValue.FromList(
            [SandboxValue.FromInt32(20), SandboxValue.FromInt32(21)],
            SandboxType.I32);

        var result = await kernel.InvokeServerExtensionAsync([ids]);

        var list = Assert.IsType<ListValue>(result);
        Assert.Equal(2, list.Values.Count);
        AssertKill(list.Values[0], 20, true);
        AssertKill(list.Values[1], 21, false);
    }

    [Fact]
    public void Async_host_binding_metadata_derives_runtime_async_manifest_requirements()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            AsyncHostBindingSource,
            "Sample.AsyncLevelPluginPackage");

        Assert.Contains("game.world.monster.read.level", package.Manifest.RequiredCapabilities);
        Assert.Contains(RuntimeCapabilityIds.Async, package.Manifest.RequiredCapabilities);
        Assert.Contains("Concurrency", package.Manifest.Effects);
    }

    [Fact]
    public async Task String_literals_escape_json_control_characters()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(
            ControlStringSource,
            "Sample.ControlStringPluginPackage");

        using var server = PluginServer.Create(defaultPolicy: PurePolicy());
        var kernel = await server.InstallServerExtensionAsync(package);

        var result = await kernel.InvokeServerExtensionAsync([]);

        var text = Assert.IsType<StringValue>(result);
        Assert.Equal("\b\f", text.Value);
    }

    [Theory]
    [InlineData("double.NaN")]
    [InlineData("double.PositiveInfinity")]
    [InlineData("double.NegativeInfinity")]
    public void Non_finite_double_literals_are_rejected_by_rpc_analyzer(string literal)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics($$"""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("bad-f64")]
            public sealed partial class BadF64Kernel
            {
                public double Read(HookContext ctx)
                {
                    return {{literal}};
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("finite", StringComparison.OrdinalIgnoreCase));
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
            CompiledBinding.RuntimeStub("DotBoxD.Kernels.Runtime.CompiledRuntime", "CallBinding"),
            GrantValidator: static (_, _) => { }));

    private static SandboxPolicy KillPolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .Grant("game.world.monster.write.*", new { }, SandboxEffect.HostStateWrite)
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();

    private static SandboxPolicy PurePolicy()
        => SandboxPolicyBuilder.Create()
            .GrantLogging()
            .WithFuel(100_000)
            .WithMaxHostCalls(10_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build();
}
