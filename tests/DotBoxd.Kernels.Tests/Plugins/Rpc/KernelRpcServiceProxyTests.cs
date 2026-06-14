using DotBoxd.Kernels;
using DotBoxd.Plugins;

namespace DotBoxd.Kernels.Tests;

/// <summary>The client-facing contract for the batch kernel; its return type drives result marshaling.</summary>
public interface IMonsterKillerService
{
    List<KillResult> KillMonsters(List<int> monsterIds);
}

/// <summary>A complex result object returned per monster — proves DTOs and lists of DTOs marshal back.</summary>
public readonly record struct KillResult(int MonsterId, bool Success);

/// <summary>Marker kernel type; <see cref="BatchKillerPluginPackage"/> is its generated-shaped package.</summary>
public sealed class BatchKillerKernel;

/// <summary>Stands in for the generator output so <c>KernelPackageRegistry.Resolve</c> finds it by name.</summary>
public static class BatchKillerPluginPackage
{
    public static PluginPackage Create() => RpcKernelTestPackages.MonsterKiller();
}

/// <summary>
/// Proves the typed kernel RPC service surface (Followup #2.4): a service interface is implemented by a
/// runtime proxy that marshals C# arguments into the sandbox, runs the batch kernel request/response in
/// one roundtrip, and marshals the result — including a <c>List&lt;KillResult&gt;</c> of DTOs — back to
/// real C# objects. Covers both the proxy over a generated kernel and the
/// <c>RegisterRpcServiceAsync</c>/<c>RpcService</c> registration sugar, plus the marshaller round-trips.
/// </summary>
public sealed class KernelRpcServiceProxyTests
{
    private const string MonsterKillerSource = """
        using System.Collections.Generic;
        using DotBoxd.Kernels;
        using DotBoxd.Plugins;
        using DotBoxd.Abstractions;

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
                    results.Add(new KillResult(id, ctx.Host<IGameWorld>().Kill(id)));
                return results;
            }
        }
        """;

    [Fact]
    public async Task A_typed_proxy_over_a_generated_kernel_returns_dtos_as_real_objects()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create(MonsterKillerSource, "Sample.MonsterKillerPluginPackage");
        using var server = PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        var kernel = await server.InstallRpcAsync(package);

        var service = KernelRpcServiceProxy.Create<IMonsterKillerService>(kernel);
        var results = service.KillMonsters([4, 5, 6]);

        Assert.Equal(3, results.Count);
        Assert.Equal(new KillResult(4, true), results[0]);
        Assert.Equal(new KillResult(5, false), results[1]);
        Assert.Equal(new KillResult(6, true), results[2]);
    }

    [Fact]
    public async Task RegisterRpcService_then_RpcService_invokes_the_batch_kernel_by_contract()
    {
        using var server = PluginServer.Create(configureHost: RpcKernelTestPackages.AddKillBinding, defaultPolicy: RpcKernelTestPackages.KillPolicy());
        await server.RegisterRpcServiceAsync<IMonsterKillerService, BatchKillerKernel>();

        var results = server.RpcService<IMonsterKillerService>().KillMonsters([1, 2]);

        Assert.Equal([new KillResult(1, false), new KillResult(2, true)], results);
    }

    [Fact]
    public void Marshaller_round_trips_dtos_and_lists_of_dtos()
    {
        var original = new List<KillResult> { new(7, true), new(8, false) };
        var sandbox = KernelRpcMarshaller.ToSandboxValue(original, typeof(List<KillResult>));

        var list = Assert.IsType<ListValue>(sandbox);
        Assert.Equal(2, list.Values.Count);
        var firstRecord = Assert.IsType<RecordValue>(list.Values[0]);
        Assert.Equal([SandboxValue.FromInt32(7), SandboxValue.FromBool(true)], firstRecord.Fields);

        var roundTripped = (List<KillResult>)KernelRpcMarshaller.FromSandboxValue(sandbox, typeof(List<KillResult>))!;
        Assert.Equal(original, roundTripped);
    }
}
